using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Domain.Customers;
using Domain.Grouping;
using Domain.Orders;

namespace Application.Network.PickupRoutes;

/// <summary>
/// A zone's route as the hub plans the day: when it next leaves (<see cref="NextPickupDay"/>, in the tenant's time
/// zone) and how much is waiting for it.
/// </summary>
public sealed record PickupRouteSummary(
    long Id,
    string Zone,
    string Hub,
    TimeOnly PickupTime,
    DateOnly NextPickupDay,
    int Stops,
    int Orders,
    int Packages);

/// <summary>
/// One waiting order at a stop. Orders in a next-day delivery (Deliver fast, Don't hold, Ship now, or one that joined
/// such a delivery) are <see cref="Urgent"/>: they must not miss this run. <see cref="Waits"/> is what the order still
/// waits for from the customer: one waiting for the fee in advance stays at the shop, one waiting to be confirmed is
/// collected and only flagged.
/// </summary>
public sealed record PickupStopOrder(string Number, int Packages, bool Urgent, CustomerStep Waits = CustomerStep.None)
{
    /// <summary>False while the order waits for its delivery fee in advance: the collector leaves it at the shop.</summary>
    public bool Collect => Waits != CustomerStep.PayInAdvance;

    public IReadOnlyList<PackageLabel> Labels =>
        [.. Enumerable.Range(1, Packages).Select(sequence => new PackageLabel(Number, sequence))];
}

/// <summary>A merchant pickup point the collector visits, with every parcel waiting there.</summary>
public sealed record PickupStop(
    string Merchant,
    string PickupPoint,
    string Address,
    string ContactPhone,
    IReadOnlyList<PickupStopOrder> Orders)
{
    public int Packages => Orders.Where(order => order.Collect).Sum(order => order.Packages);
}

/// <summary>The collector's sheet: where to go, what to collect, where to bring it.</summary>
public sealed record PickupRouteSheet(
    long Id,
    string Zone,
    string Hub,
    string HubAddress,
    TimeOnly PickupTime,
    DateOnly NextPickupDay,
    IReadOnlyList<PickupStop> Stops)
{
    public int Packages => Stops.Sum(stop => stop.Packages);
}

/// <summary>
/// Pickup routes for hub staff. A route's stops are the pickup points in its zone with orders the route has not
/// collected yet (<see cref="OrderStatus.Created"/>); the zone is the pickup point's, since that is where the
/// parcels are. Worked out when asked, so an order placed a minute before the run is on the sheet.
/// </summary>
public class PickupRoutesHandler(IAppDbContext db, ITenantContext tenantContext, TimeProvider time)
{
    public async Task<IReadOnlyList<PickupRouteSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        var timeZone = TenantTimeZone();
        var now = time.GetUtcNow().UtcDateTime;

        var routes = await db.PickupRoutes
            .Where(route => !route.Archived)
            .Include(route => route.Zone!.Hub)
            .OrderBy(route => route.Zone!.Name)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var waiting = await (
            from package in db.Packages
            join order in db.Orders on package.OrderId equals order.Id
            join point in db.PickupPoints on order.PickupPointId equals point.Id
            join area in db.Areas on point.AreaId equals area.Id
            where order.Status == OrderStatus.Created &&
                (order.CustomerStep != CustomerStep.PayInAdvance || order.ConfirmedOn != null)
            group new { OrderId = order.Id, order.PickupPointId } by area.ZoneId into zone
            select new
            {
                ZoneId = zone.Key,
                Stops = zone.Select(row => row.PickupPointId).Distinct().Count(),
                Orders = zone.Select(row => row.OrderId).Distinct().Count(),
                Packages = zone.Count()
            })
            .ToDictionaryAsync(zone => zone.ZoneId, cancellationToken);

        return
        [
            .. routes.Select(route =>
            {
                var load = waiting.GetValueOrDefault(route.ZoneId);

                return new PickupRouteSummary(
                    route.Id,
                    route.Zone!.Name,
                    route.Zone.Hub!.Name,
                    route.PickupTime,
                    LocalDay(route.NextPickup(now, timeZone), timeZone),
                    load?.Stops ?? 0,
                    load?.Orders ?? 0,
                    load?.Packages ?? 0);
            })
        ];
    }

    /// <summary>The route's sheet, or null when it is not this tenant's route (the query filter hides it).</summary>
    public async Task<PickupRouteSheet?> GetAsync(long routeId, CancellationToken cancellationToken = default)
    {
        var route = await db.PickupRoutes
            .Where(route => route.Id == routeId && !route.Archived)
            .Include(route => route.Zone!.Hub)
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);
        if (route is null)
        {
            return null;
        }

        var orders = await (
            from order in db.Orders
            join point in db.PickupPoints on order.PickupPointId equals point.Id
            join area in db.Areas on point.AreaId equals area.Id
            join merchant in db.Merchants on order.MerchantId equals merchant.Id
            join g in db.DeliveryGroups on order.DeliveryGroupId equals g.Id
            where order.Status == OrderStatus.Created && area.ZoneId == route.ZoneId
            orderby merchant.Name, point.Id, order.Id
            select new
            {
                PointId = point.Id,
                Merchant = merchant.Name,
                Point = point.Name,
                point.Address,
                point.ContactPhone,
                Order = new PickupStopOrder(
                    order.Number,
                    order.Packages.Count,
                    g.Kind != DeliveryGroupKind.Waiting,
                    order.ConfirmedOn == null ? order.CustomerStep : CustomerStep.None)
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var timeZone = TenantTimeZone();
        var stops = orders
            .GroupBy(row => row.PointId)
            .Select(stop =>
            {
                var first = stop.First();

                return new PickupStop(
                    first.Merchant,
                    first.Point,
                    first.Address,
                    first.ContactPhone,
                    [.. stop.Select(row => row.Order)]);
            });

        return new PickupRouteSheet(
            route.Id,
            route.Zone!.Name,
            route.Zone.Hub!.Name,
            route.Zone.Hub.Address,
            route.PickupTime,
            LocalDay(route.NextPickup(time.GetUtcNow().UtcDateTime, timeZone), timeZone),
            [.. stops]);
    }

    private TimeZoneInfo TenantTimeZone()
    {
        var tenant = tenantContext.Tenant ?? throw new InvalidOperationException("Pickup routes need a tenant.");

        return TimeZoneInfo.FindSystemTimeZoneById(tenant.TimeZone);
    }

    private static DateOnly LocalDay(DateTime utc, TimeZoneInfo timeZone)
    {
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, timeZone));
    }
}
