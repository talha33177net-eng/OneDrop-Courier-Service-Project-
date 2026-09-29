using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Application.Delivery.PlanTrips;
using Domain.Delivery;
using Domain.Grouping;

namespace Application.Delivery.HubTrips;

/// <summary>
/// A delivery on a rider's trip: how many of its parcels are at the hub and, once the trip has left, how many the
/// rider took.
/// </summary>
public sealed record TripStopRow(string Delivery, string Area, string? Shelf, int ParcelsHere, int ParcelsTaken, int Parcels);

/// <summary>
/// A rider of the hub and their trip today; <see cref="Status"/> is null while they have none. <see cref="Carried"/> is
/// what the planned deliveries put on the bike, or what the rider took once the trip has left.
/// </summary>
public sealed record RiderTripRow(
    string Rider,
    TripStatus? Status,
    TripLoad Carried,
    TripLoad Limit,
    IReadOnlyList<TripStopRow> Stops);

/// <summary>A closed delivery due out today that is on no trip: its parcels are not here yet, or no bike has room.</summary>
public sealed record WaitingRow(string Delivery, string Area, DateOnly DueOn, int ParcelsHere, int Parcels, int WeightGrams);

public sealed record HubTrips(DateOnly Day, IReadOnlyList<RiderTripRow> Riders, IReadOnlyList<WaitingRow> Waiting);

/// <summary>
/// Today's trips at one hub for its staff: each rider's deliveries against their bike's limit, and the deliveries
/// due today that are on no trip. Staff can plan again at once instead of waiting for the next run of the job.
/// </summary>
public class HubTripsHandler(IAppDbContext db, ITenantContext tenantContext, TimeProvider time, TripPlanning planning)
{
    /// <summary>Null for a hub that is not this operator's.</summary>
    public async Task<HubTrips?> TodayAsync(string hubCode, CancellationToken cancellationToken = default)
    {
        var hub = await db.Hubs.AsNoTracking().FirstOrDefaultAsync(h => h.Code == hubCode && !h.Archived, cancellationToken);
        if (hub is null)
        {
            return null;
        }

        var timeZone = TenantTimeZone();
        var now = time.GetUtcNow().UtcDateTime;
        var today = timeZone.LocalDay(now);

        var riders = await (
            from rider in db.Riders
            join t in db.Trips.Where(t => t.DeliveryDate == today) on rider.Id equals t.RiderId into trips
            from trip in trips.DefaultIfEmpty()
            where rider.HubId == hub.Id && (!rider.Archived || trip != null)
            orderby rider.Name, rider.Id
            select new { rider.Name, Limit = new TripLoad(rider.MaxParcels, rider.MaxWeightGrams), Trip = trip })
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var tripIds = riders.Where(r => r.Trip != null).Select(r => r.Trip!.Id).ToList();
        var stops = await (
            from stop in db.TripStops
            join g in db.DeliveryGroups on stop.DeliveryGroupId equals g.Id
            join address in db.CustomerAddresses on g.AddressId equals address.Id
            join area in db.Areas on address.AreaId equals area.Id
            where tripIds.Contains(stop.TripId)
            orderby area.Name, g.Number
            select new { stop.TripId, GroupId = g.Id, g.Number, g.Shelf, Area = area.Name })
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var waiting = await (
            from g in db.DeliveryGroups
            join address in db.CustomerAddresses on g.AddressId equals address.Id
            join area in db.Areas on address.AreaId equals area.Id
            where g.HubId == hub.Id &&
                g.Status == DeliveryGroupStatus.Locked &&
                g.LocksAt <= now &&
                !db.TripStops.Any(stop => stop.DeliveryGroupId == g.Id && stop.DeliveryDate == today)
            orderby g.LocksAt, area.Name, g.Number
            select new { g.Id, g.Number, g.LocksAt, Area = area.Name })
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var parcels = await db.ParcelsAsync(
            [.. stops.Select(stop => stop.GroupId), .. waiting.Select(w => w.Id)],
            cancellationToken);

        return new HubTrips(
            today,
            [
                .. riders.Select(rider =>
                {
                    var onTrip = stops.Where(stop => stop.TripId == rider.Trip?.Id).ToList();

                    return new RiderTripRow(
                        rider.Name,
                        rider.Trip?.Status,
                        onTrip.Aggregate(
                            TripLoad.None,
                            (load, stop) => load + (rider.Trip?.Status == TripStatus.Planned
                                ? ParcelsOf(stop.GroupId).Load
                                : ParcelsOf(stop.GroupId).Taken)),
                        rider.Limit,
                        [
                            .. onTrip.Select(stop => new TripStopRow(
                                stop.Number,
                                stop.Area,
                                stop.Shelf is { } shelf ? DeliveryGroup.ShelfCode(hub.Code, shelf) : null,
                                ParcelsOf(stop.GroupId).AtHub,
                                ParcelsOf(stop.GroupId).Taken.Parcels,
                                ParcelsOf(stop.GroupId).Load.Parcels))
                        ]);
                })
            ],
            [
                .. waiting.Select(w => new WaitingRow(
                    w.Number,
                    w.Area,
                    timeZone.LocalDay(w.LocksAt),
                    ParcelsOf(w.Id).AtHub,
                    ParcelsOf(w.Id).Load.Parcels,
                    ParcelsOf(w.Id).Load.WeightGrams))
            ]);

        DeliveryParcels ParcelsOf(long groupId)
        {
            return parcels.GetValueOrDefault(groupId, DeliveryParcels.None);
        }
    }

    /// <summary>Plans the hub's trips now. Null for a hub that is not this operator's.</summary>
    public async Task<TripPlanningResult?> PlanAsync(string hubCode, CancellationToken cancellationToken = default)
    {
        var hubId = await db.Hubs
            .Where(h => h.Code == hubCode && !h.Archived)
            .Select(h => (long?)h.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return hubId is null ? null : await planning.PlanAsync(hubId, cancellationToken);
    }

    private TimeZoneInfo TenantTimeZone()
    {
        var tenant = tenantContext.Tenant ?? throw new InvalidOperationException("Trips need a tenant.");

        return TimeZoneInfo.FindSystemTimeZoneById(tenant.TimeZone);
    }
}
