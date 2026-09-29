using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Domain.Delivery;
using Domain.Grouping;
using Domain.Orders;
using Domain.Pricing;

namespace Application.Delivery;

/// <summary>One delivery at a visit: its stop on the trip, the delivery and its orders (with their packages).</summary>
internal sealed record VisitDelivery(TripStop Stop, DeliveryGroup Group, IReadOnlyList<Order> Orders)
{
    /// <summary>
    /// The orders the rider carries to the door: before the trip leaves, every order still for delivery; once it is
    /// out, the orders handed over at the start (orders left behind then moved to a later delivery).
    /// </summary>
    public IEnumerable<Order> Carried(TripStatus trip)
    {
        return Orders.Where(order => trip == TripStatus.Planned
            ? Order.IsForDelivery(order.Status)
            : order.Status != OrderStatus.Cancelled);
    }
}

/// <summary>Where a visit goes and who to ask for.</summary>
internal sealed record VisitPlace(string? Name, string Phone, string Address, string? Landmark, string Area);

/// <summary>
/// One door on a trip: every delivery on it for the same customer (phone) in the same area. The rider goes once and
/// the customer pays one fee (<see cref="DeliveryFeeCalculator.VisitFees"/>), so an address spelt two ways never
/// costs two deliveries. <see cref="Key"/>, its first delivery's number, names the visit on the rider's screen.
/// </summary>
internal sealed record Visit(VisitPlace Place, IReadOnlyList<VisitDelivery> Deliveries)
{
    public string Key => Deliveries[0].Group.Number;

    /// <summary>What happened at the door; null while the visit is still to do. Its deliveries are done together.</summary>
    public StopOutcome? Outcome => Deliveries[0].Stop.Outcome;
}

internal static class TripVisitsQuery
{
    /// <summary>
    /// The rider signed in as <paramref name="userId"/> and their trip on <paramref name="day"/>, tracked. The rider is
    /// null when the user is not one of the operator's riders; the trip when they have none that day.
    /// </summary>
    public static async Task<(Rider? Rider, Trip? Trip)> TodaysTripAsync(
        this IAppDbContext db,
        long userId,
        DateOnly day,
        CancellationToken cancellationToken)
    {
        var rider = await db.Riders.AsNoTracking().FirstOrDefaultAsync(r => r.UserId == userId, cancellationToken);
        if (rider is null)
        {
            return (null, null);
        }

        var trip = await db.Trips.FirstOrDefaultAsync(t => t.RiderId == rider.Id && t.DeliveryDate == day, cancellationToken);

        return (rider, trip);
    }

    /// <summary>
    /// The trip's visits, tracked so a door action can change them: neighbours together (area, then address), and
    /// within a visit its deliveries by number.
    /// </summary>
    public static async Task<IReadOnlyList<Visit>> VisitsAsync(
        this IAppDbContext db,
        long tripId,
        CancellationToken cancellationToken)
    {
        var stops = await db.TripStops.Where(stop => stop.TripId == tripId).ToListAsync(cancellationToken);
        var groupIds = stops.Select(stop => stop.DeliveryGroupId).ToList();
        var groups = await db.DeliveryGroups.Where(g => groupIds.Contains(g.Id)).ToListAsync(cancellationToken);
        var orders = await db.Orders
            .Include(order => order.Packages)
            .Where(order => groupIds.Contains(order.DeliveryGroupId))
            .ToListAsync(cancellationToken);
        var places = await (
            from g in db.DeliveryGroups
            join customer in db.Customers on g.CustomerId equals customer.Id
            join address in db.CustomerAddresses on g.AddressId equals address.Id
            join area in db.Areas on address.AreaId equals area.Id
            where groupIds.Contains(g.Id)
            select new
            {
                GroupId = g.Id,
                g.CustomerId,
                AreaId = area.Id,
                customer.Name,
                customer.Phone,
                Address = address.Line2 == null ? address.Line1 : address.Line1 + ", " + address.Line2,
                address.Landmark,
                Area = area.Name
            })
            .AsNoTracking()
            .ToDictionaryAsync(place => place.GroupId, cancellationToken);

        return
        [
            .. stops
                .Select(stop => new VisitDelivery(
                    stop,
                    groups.Single(g => g.Id == stop.DeliveryGroupId),
                    [.. orders.Where(order => order.DeliveryGroupId == stop.DeliveryGroupId).OrderBy(order => order.Number)]))
                .GroupBy(delivery => (places[delivery.Group.Id].CustomerId, places[delivery.Group.Id].AreaId))
                .Select(door =>
                {
                    var deliveries = door.OrderBy(delivery => delivery.Group.Number).ToList();
                    var spots = deliveries.Select(delivery => places[delivery.Group.Id]).ToList();

                    return new Visit(
                        new VisitPlace(
                            spots[0].Name,
                            spots[0].Phone,
                            string.Join(" / ", spots.Select(place => place.Address).Distinct()),
                            spots.Select(place => place.Landmark).FirstOrDefault(landmark => landmark is not null),
                            spots[0].Area),
                        deliveries);
                })
                .OrderBy(visit => visit.Place.Area)
                .ThenBy(visit => visit.Place.Address)
                .ThenBy(visit => visit.Key)
        ];
    }
}
