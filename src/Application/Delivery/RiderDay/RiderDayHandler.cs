using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Domain.Common;
using Domain.Customers;
using Domain.Delivery;
using Domain.Grouping;
using Domain.Orders;
using Domain.Pricing;

namespace Application.Delivery.RiderDay;

/// <summary>
/// One shop's order at a stop. <see cref="Ready"/> while the trip is planned: every parcel is on the shelf. Once the
/// trip has left, only the orders the rider took are listed.
/// </summary>
public sealed record RiderStopOrder(string Number, string Shop, OrderStatus Status, IReadOnlyList<string> Labels, bool Ready);

/// <summary>
/// A stop on the rider's trip: one delivery, with where to take it and what to collect at the door (the delivery fee
/// on what is delivered, and the shops' cash on delivery).
/// </summary>
public sealed record RiderStop(
    int Number,
    string Delivery,
    DeliveryGroupStatus Status,
    string? Shelf,
    string Recipient,
    string Phone,
    string Address,
    string? Landmark,
    string Area,
    IReadOnlyList<RiderStopOrder> Orders,
    decimal Fee,
    decimal Cod)
{
    public decimal ToCollect => Fee + Cod;

    public int Parcels => Orders.Sum(order => order.Labels.Count);
}

/// <summary>
/// The rider's day: their trip for today (none yet while <see cref="Status"/> is null) and its stops, neighbours
/// together, against the bike's limit.
/// </summary>
public sealed record RiderToday(
    string Rider,
    string Hub,
    DateOnly Day,
    TripStatus? Status,
    TripLoad Limit,
    IReadOnlyList<RiderStop> Stops)
{
    public decimal ToCollect => Stops.Sum(stop => stop.ToCollect);
}

/// <summary>What starting the trip did: deliveries taken out, and deliveries left at the hub with nothing ready.</summary>
public sealed record StartedTrip(int Deliveries, int Orders, int LeftBehind);

/// <summary>
/// The signed-in rider's screen. Starting the trip hands over every order whose parcels are all on the shelf
/// (<see cref="Order.HandToRider"/>): its delivery goes out and frees its shelf. An order not ready stays at the hub;
/// a delivery with nothing ready comes off the trip and waits for the next one.
/// </summary>
public class RiderDayHandler(IAppDbContext db, ITenantContext tenantContext, TimeProvider time)
{
    public static Error NoTrip => Error.NotFound("trip.none", "You have no trip today.");

    /// <summary>Null when the user is not one of the operator's riders.</summary>
    public async Task<RiderToday?> TodayAsync(long userId, CancellationToken cancellationToken = default)
    {
        var rider = await db.Riders.AsNoTracking().FirstOrDefaultAsync(r => r.UserId == userId, cancellationToken);
        if (rider is null)
        {
            return null;
        }

        var (tenant, timeZone) = TenantAndTimeZone();
        var today = timeZone.LocalDay(time.GetUtcNow().UtcDateTime);
        var hub = await db.Hubs.AsNoTracking().SingleAsync(h => h.Id == rider.HubId, cancellationToken);
        var trip = await db.Trips
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.RiderId == rider.Id && t.DeliveryDate == today, cancellationToken);
        if (trip is null)
        {
            return new RiderToday(rider.Name, hub.Name, today, null, rider.Limit, []);
        }

        var stops = await (
            from stop in db.TripStops
            join g in db.DeliveryGroups on stop.DeliveryGroupId equals g.Id
            join customer in db.Customers on g.CustomerId equals customer.Id
            join address in db.CustomerAddresses on g.AddressId equals address.Id
            join area in db.Areas on address.AreaId equals area.Id
            where stop.TripId == trip.Id
            orderby area.Name, address.Line1, g.Number
            select new
            {
                GroupId = g.Id,
                g.Number,
                g.Status,
                g.Kind,
                g.Shelf,
                customer.Phone,
                customer.Name,
                Address = address.Line2 == null ? address.Line1 : address.Line1 + ", " + address.Line2,
                address.Landmark,
                Area = area.Name
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var groupIds = stops.Select(stop => stop.GroupId).ToList();
        var inDeliveries = await db.Orders
            .Include(order => order.Packages)
            .Where(order => groupIds.Contains(order.DeliveryGroupId))
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var merchantIds = inDeliveries.Select(order => order.MerchantId).Distinct().ToList();
        var shops = await db.Merchants
            .Where(merchant => merchantIds.Contains(merchant.Id))
            .ToDictionaryAsync(merchant => merchant.Id, merchant => merchant.Name, cancellationToken);
        var orders = inDeliveries
            .Select(order => new { Order = order, Shop = shops[order.MerchantId] })
            .OrderBy(row => row.Shop)
            .ThenBy(row => row.Order.Number)
            .ToLookup(row => row.Order.DeliveryGroupId);
        var fees = new DeliveryFeeCalculator(tenant.Fees);

        return new RiderToday(
            rider.Name,
            hub.Name,
            today,
            trip.Status,
            rider.Limit,
            [
                .. stops.Select((stop, index) =>
                {
                    // Before the trip leaves, every order still for delivery; afterwards, the orders the rider took
                    var taken = orders[stop.GroupId]
                        .Where(row => trip.Status == TripStatus.Planned
                            ? Order.IsForDelivery(row.Order.Status)
                            : row.Order.Status is OrderStatus.OutForDelivery or OrderStatus.Delivered)
                        .ToList();

                    return new RiderStop(
                        index + 1,
                        stop.Number,
                        stop.Status,
                        stop.Shelf is { } shelf ? DeliveryGroup.ShelfCode(hub.Code, shelf) : null,
                        string.Join(" / ", taken.Select(row => row.Order.RecipientName).Distinct().DefaultIfEmpty(stop.Name ?? "")),
                        PhoneNumber.Parse(stop.Phone).Value.Local,
                        stop.Address,
                        stop.Landmark,
                        stop.Area,
                        [
                            .. taken.Select(row => new RiderStopOrder(
                                row.Order.Number,
                                row.Shop,
                                row.Order.Status,
                                [.. row.Order.Packages.OrderBy(p => p.Sequence).Select(p => new PackageLabel(row.Order.Number, p.Sequence).ToString())],
                                row.Order.IsReadyAt(hub.Id)))
                        ],
                        fees.GroupFee(
                            taken.Select(row => new FeeLine(
                                row.Order.MerchantId,
                                row.Order.Speed,
                                row.Order.Status,
                                row.Order.TotalWeightGrams)),
                            stop.Kind == DeliveryGroupKind.ShippedNow),
                        taken.Sum(row => row.Order.CodAmount));
                })
            ]);
    }

    /// <summary>The rider leaves with every order ready on the shelves. Refused when nothing on the trip is ready.</summary>
    public async Task<Result<StartedTrip>> StartAsync(long userId, CancellationToken cancellationToken = default)
    {
        var rider = await db.Riders.AsNoTracking().FirstOrDefaultAsync(r => r.UserId == userId, cancellationToken);
        if (rider is null)
        {
            return NoTrip;
        }

        var now = time.GetUtcNow().UtcDateTime;
        var today = TenantAndTimeZone().TimeZone.LocalDay(now);
        var trip = await db.Trips.FirstOrDefaultAsync(t => t.RiderId == rider.Id && t.DeliveryDate == today, cancellationToken);
        if (trip is null)
        {
            return NoTrip;
        }

        if (trip.Status != TripStatus.Planned)
        {
            return Trip.NotPlanned;
        }

        var stops = await db.TripStops.Where(stop => stop.TripId == trip.Id).ToListAsync(cancellationToken);
        var groupIds = stops.Select(stop => stop.DeliveryGroupId).ToList();
        var groups = await db.DeliveryGroups.Where(g => groupIds.Contains(g.Id)).ToListAsync(cancellationToken);
        var orders = await db.Orders
            .Include(order => order.Packages)
            .Where(order => groupIds.Contains(order.DeliveryGroupId))
            .ToListAsync(cancellationToken);

        var taken = 0;
        var leftBehind = 0;
        foreach (var stop in stops)
        {
            var handed = orders
                .Where(order => order.DeliveryGroupId == stop.DeliveryGroupId)
                .Count(order => order.HandToRider(trip.HubId));
            if (handed == 0)
            {
                db.TripStops.Remove(stop);
                leftBehind++;

                continue;
            }

            groups.Single(g => g.Id == stop.DeliveryGroupId).MoveTo(DeliveryGroupStatus.Dispatched, now);
            taken += handed;
        }

        if (taken == 0)
        {
            return Error.Conflict("trip.nothingReady", "No parcel on this trip is on its shelf yet.");
        }

        trip.Start(now);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict("trip.changed", "The trip changed while you were starting it. Open it again.");
        }

        return new StartedTrip(stops.Count - leftBehind, taken, leftBehind);
    }

    private (TenantInfo Tenant, TimeZoneInfo TimeZone) TenantAndTimeZone()
    {
        var tenant = tenantContext.Tenant ?? throw new InvalidOperationException("Riders need a tenant.");

        return (tenant, TimeZoneInfo.FindSystemTimeZoneById(tenant.TimeZone));
    }
}
