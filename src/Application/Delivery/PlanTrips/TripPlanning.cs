using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Application.Abstractions;
using Domain.Delivery;
using Domain.Grouping;

namespace Application.Delivery.PlanTrips;

/// <summary>What one planning run did: deliveries put on a trip, and deliveries no bike had room for.</summary>
public sealed record TripPlanningResult(int Planned, int NoRoom);

/// <summary>
/// Puts the closed deliveries due out today on the riders of their hub (<see cref="TripPlanner"/>), within each
/// bike's limit. Due means delivery day has started (<see cref="DeliveryGroup.LocksAt"/> has passed) and at least one
/// parcel is on the delivery's shelf; days are the tenant's. A rider gets one trip a day and takes deliveries until
/// they leave; running again adds what has become ready since and changes nothing already planned. A trip left
/// planned on an earlier day is cancelled so its deliveries go out today. Each stop is saved on its own: one that a
/// planner running at the same moment took first is skipped (the database keeps a delivery on one trip a day).
/// </summary>
public class TripPlanning(IAppDbContext db, ITenantContext tenantContext, TimeProvider time, ILogger<TripPlanning> logger)
{
    /// <summary>Plans every hub of the tenant, or only <paramref name="hubId"/>.</summary>
    public async Task<TripPlanningResult> PlanAsync(long? hubId, CancellationToken cancellationToken = default)
    {
        var tenant = tenantContext.Tenant ?? throw new InvalidOperationException("Planning trips needs a tenant.");
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(tenant.TimeZone);
        var now = time.GetUtcNow().UtcDateTime;
        var today = timeZone.LocalDay(now);
        var hubs = hubId is { } id
            ? [id]
            : await db.Hubs.Where(hub => !hub.Archived).Select(hub => hub.Id).ToListAsync(cancellationToken);

        var result = new TripPlanningResult(0, 0);
        foreach (var hub in hubs)
        {
            await CancelPastTripsAsync(hub, today, cancellationToken);
            var planned = await PlanHubAsync(hub, now, today, timeZone, cancellationToken);
            result = new TripPlanningResult(result.Planned + planned.Planned, result.NoRoom + planned.NoRoom);
        }

        logger.LogInformation(
            "Planned {Planned} deliveries on trips; {NoRoom} had no room on any bike",
            result.Planned,
            result.NoRoom);

        return result;
    }

    private async Task CancelPastTripsAsync(long hubId, DateOnly today, CancellationToken cancellationToken)
    {
        var past = await db.Trips
            .Where(trip => trip.HubId == hubId && trip.Status == TripStatus.Planned && trip.DeliveryDate < today)
            .ToListAsync(cancellationToken);
        foreach (var trip in past)
        {
            trip.CancelIfPast(today);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Started or cancelled by someone else meanwhile
                db.Entry(trip).State = EntityState.Detached;
            }
        }
    }

    private async Task<TripPlanningResult> PlanHubAsync(
        long hubId,
        DateTime now,
        DateOnly today,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken)
    {
        var riders = await db.Riders
            .Where(rider => rider.HubId == hubId && !rider.Archived)
            .OrderBy(rider => rider.Name)
            .ThenBy(rider => rider.Id)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var trips = await db.Trips
            .Where(trip => trip.HubId == hubId && trip.DeliveryDate == today)
            .ToDictionaryAsync(trip => trip.RiderId, cancellationToken);
        var tripIds = trips.Values.Select(trip => trip.Id).ToList();
        var stops = await db.TripStops
            .Where(stop => tripIds.Contains(stop.TripId))
            .Select(stop => new { stop.TripId, stop.DeliveryGroupId })
            .ToListAsync(cancellationToken);
        var waiting = await (
            from g in db.DeliveryGroups
            join address in db.CustomerAddresses on g.AddressId equals address.Id
            join area in db.Areas on address.AreaId equals area.Id
            where g.HubId == hubId &&
                g.Status == DeliveryGroupStatus.Locked &&
                g.LocksAt <= now &&
                !db.TripStops.Any(stop => stop.DeliveryGroupId == g.Id && stop.DeliveryDate == today)
            select new { Group = g, Area = area.Name })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var parcels = await db.ParcelsAsync(
            [.. waiting.Select(w => w.Group.Id), .. stops.Select(stop => stop.DeliveryGroupId)],
            cancellationToken);
        var bikes = riders
            .Where(rider => !trips.TryGetValue(rider.Id, out var trip) || trip.Status == TripStatus.Planned)
            .Select(rider => new Bike(
                rider.Id,
                rider.Limit,
                stops
                    .Where(stop => trips.TryGetValue(rider.Id, out var trip) && stop.TripId == trip.Id)
                    .Aggregate(TripLoad.None, (load, stop) => load + LoadOf(stop.DeliveryGroupId))))
            .ToList();
        var ready = waiting
            .Where(w => parcels.TryGetValue(w.Group.Id, out var here) && here.AtHub > 0)
            .Select(w => new WaitingDelivery(w.Group.Id, timeZone.LocalDay(w.Group.LocksAt), w.Area, LoadOf(w.Group.Id)))
            .ToList();
        var plan = TripPlanner.Plan(bikes, ready);

        var planned = 0;
        foreach (var assignment in plan.Assignments)
        {
            var group = waiting.Single(w => w.Group.Id == assignment.GroupId).Group;
            if (await AddStopAsync(assignment.RiderId, group, trips, riders, today, cancellationToken))
            {
                planned++;
            }
        }

        return new TripPlanningResult(planned, plan.NoRoom.Count);

        TripLoad LoadOf(long groupId)
        {
            return parcels.TryGetValue(groupId, out var load) ? load.Load : TripLoad.None;
        }
    }

    /// <summary>False when another planner took the delivery, or the rider's trip, at the same moment.</summary>
    private async Task<bool> AddStopAsync(
        long riderId,
        DeliveryGroup group,
        Dictionary<long, Trip> trips,
        List<Rider> riders,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        if (!trips.TryGetValue(riderId, out var trip))
        {
            trip = Trip.Plan(riders.Single(rider => rider.Id == riderId), today);
            db.Trips.Add(trip);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                db.Entry(trip).State = EntityState.Detached;

                return false;
            }

            trips[riderId] = trip;
        }

        var stop = trip.Add(group);
        if (stop.IsFailure)
        {
            return false;
        }

        db.TripStops.Add(stop.Value);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            db.Entry(stop.Value).State = EntityState.Detached;

            return false;
        }

        return true;
    }
}
