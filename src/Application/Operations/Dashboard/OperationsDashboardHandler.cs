using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Application.Delivery;
using Domain.Customers;
using Domain.Delivery;
using Domain.Grouping;
using Domain.Network;
using Domain.Orders;

namespace Application.Operations.Dashboard;

/// <summary>
/// A parcel of a delivery due out today that is not on its shelf yet: its label, delivery, shop and where it is
/// (<see cref="OtherHub"/> names the hub when it is at or on its way from another one).
/// </summary>
public sealed record MissingParcel(string Label, string Delivery, string Shop, ParcelPlace Place, string? OtherHub);

/// <summary>
/// One hub now: deliveries still open to other shops, closed deliveries due out today (or late) and not out yet,
/// parcels scanned in here, parcels on the shuttle to here, riders out of all its riders, and the parcels of today's
/// deliveries that are not here yet.
/// </summary>
public sealed record HubNow(
    string Code,
    string Name,
    int OpenDeliveries,
    int DueToday,
    int ParcelsHere,
    int ParcelsOnTheWay,
    int RidersOut,
    int Riders,
    IReadOnlyList<MissingParcel> NotHereYet);

/// <summary>Seven days ending on <see cref="To"/>.</summary>
public sealed record DensityWeek(DateOnly From, DateOnly To);

/// <summary>An area's packages per delivery, one entry per week of <see cref="OperatorNow.Weeks"/>.</summary>
public sealed record AreaDensity(string Zone, string Area, IReadOnlyList<DeliveryDensity> Weeks);

/// <summary>The operator now: every hub, and packages per delivery by area, week by week (oldest week first).</summary>
public sealed record OperatorNow(
    DateOnly Day,
    IReadOnlyList<HubNow> Hubs,
    IReadOnlyList<DensityWeek> Weeks,
    IReadOnlyList<AreaDensity> Areas,
    IReadOnlyList<DeliveryDensity> Total);

/// <summary>
/// The live counts behind "Hub today" and the operator's dashboard, worked out from the data when asked. Pages read
/// them again whenever <see cref="IOperationsFeed"/> says something changed.
/// </summary>
public class OperationsDashboardHandler(IAppDbContext db, ITenantContext tenantContext, TimeProvider time)
{
    /// <summary>Null for a hub that is not this operator's.</summary>
    public async Task<HubNow?> HubAsync(string hubCode, CancellationToken cancellationToken = default)
    {
        var hubs = await HubsAsync(cancellationToken);
        var hub = hubs.FirstOrDefault(h => h.Code == hubCode && !h.Archived);

        return hub is null ? null : (await CountAsync([hub], hubs, cancellationToken)).Single();
    }

    /// <summary>Every hub of the operator, and packages per delivery for the last <paramref name="weeks"/> weeks.</summary>
    public async Task<OperatorNow> OperatorAsync(int weeks, CancellationToken cancellationToken = default)
    {
        var hubs = await HubsAsync(cancellationToken);
        var counts = await CountAsync([.. hubs.Where(h => !h.Archived).OrderBy(h => h.Name)], hubs, cancellationToken);
        var today = TenantTimeZone().LocalDay(time.GetUtcNow().UtcDateTime);
        var firstDay = today.AddDays(-7 * weeks + 1);

        var delivered = await (
            from stop in db.TripStops
            join g in db.DeliveryGroups on stop.DeliveryGroupId equals g.Id
            join address in db.CustomerAddresses on g.AddressId equals address.Id
            join area in db.Areas on address.AreaId equals area.Id
            join zone in db.Zones on area.ZoneId equals zone.Id
            where stop.Outcome == StopOutcome.Delivered && stop.DeliveryDate >= firstDay && stop.DeliveryDate <= today
            select new
            {
                stop.DeliveryDate,
                Zone = zone.Name,
                Area = area.Name,
                Packages = db.Packages.Count(p => db.Orders.Any(o =>
                    o.Id == p.OrderId && o.DeliveryGroupId == g.Id && o.Status == OrderStatus.Delivered))
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var areas = delivered
            .Where(row => row.Packages > 0)
            .GroupBy(row => (row.Zone, row.Area))
            .OrderBy(area => area.Key.Zone)
            .ThenBy(area => area.Key.Area)
            .Select(area => new AreaDensity(area.Key.Zone, area.Key.Area, ByWeek(area.Select(row => (row.DeliveryDate, row.Packages)))))
            .ToList();

        return new OperatorNow(
            today,
            counts,
            [.. Enumerable.Range(0, weeks).Select(week => today.AddDays(-7 * (weeks - 1 - week))).Select(to => new DensityWeek(to.AddDays(-6), to))],
            areas,
            [.. Enumerable.Range(0, weeks).Select(week => areas.Aggregate(DeliveryDensity.None, (total, area) => total + area.Weeks[week]))]);

        IReadOnlyList<DeliveryDensity> ByWeek(IEnumerable<(DateOnly Day, int Packages)> rows)
        {
            var byWeek = new DeliveryDensity[weeks];
            foreach (var (day, packages) in rows)
            {
                var week = weeks - 1 - DeliveryDensity.WeeksBack(today, day);
                byWeek[week] += new DeliveryDensity(1, packages);
            }

            return byWeek;
        }
    }

    private async Task<List<Hub>> HubsAsync(CancellationToken cancellationToken)
    {
        return await db.Hubs.AsNoTracking().ToListAsync(cancellationToken);
    }

    private async Task<List<HubNow>> CountAsync(IReadOnlyList<Hub> hubs, IReadOnlyList<Hub> allHubs, CancellationToken cancellationToken)
    {
        var ids = hubs.Select(h => h.Id).ToList();
        var now = time.GetUtcNow().UtcDateTime;
        var today = TenantTimeZone().LocalDay(now);

        var open = await db.DeliveryGroups
            .Where(g => ids.Contains(g.HubId) && g.Status == DeliveryGroupStatus.Open)
            .GroupBy(g => g.HubId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        var due = await db.DeliveryGroups
            .Where(g => ids.Contains(g.HubId) && g.Status == DeliveryGroupStatus.Locked && g.LocksAt <= now)
            .GroupBy(g => g.HubId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        var here = await db.Packages
            .Where(p => p.HubId != null && ids.Contains(p.HubId.Value))
            .GroupBy(p => p.HubId!.Value)
            .Select(p => new { p.Key, Count = p.Count() })
            .ToDictionaryAsync(p => p.Key, p => p.Count, cancellationToken);
        var onTheWay = await db.Packages
            .Where(p => p.ShuttleToHubId != null && ids.Contains(p.ShuttleToHubId.Value))
            .GroupBy(p => p.ShuttleToHubId!.Value)
            .Select(p => new { p.Key, Count = p.Count() })
            .ToDictionaryAsync(p => p.Key, p => p.Count, cancellationToken);
        var riders = await db.Riders
            .Where(r => !r.Archived && ids.Contains(r.HubId))
            .GroupBy(r => r.HubId)
            .Select(r => new { r.Key, Count = r.Count() })
            .ToDictionaryAsync(r => r.Key, r => r.Count, cancellationToken);
        var riding = await db.Trips
            .Where(t => ids.Contains(t.HubId) && t.DeliveryDate == today && t.Status == TripStatus.Out)
            .GroupBy(t => t.HubId)
            .Select(t => new { t.Key, Count = t.Count() })
            .ToDictionaryAsync(t => t.Key, t => t.Count, cancellationToken);

        // Parcels of deliveries due out now whose package is not at the delivery's hub
        var away = await (
            from package in db.Packages
            join order in db.Orders on package.OrderId equals order.Id
            join g in db.DeliveryGroups on order.DeliveryGroupId equals g.Id
            join merchant in db.Merchants on order.MerchantId equals merchant.Id
            where ids.Contains(g.HubId) &&
                g.Status == DeliveryGroupStatus.Locked &&
                g.LocksAt <= now &&
                package.HubId != g.HubId
            orderby g.LocksAt, g.Number, order.Number, package.Sequence
            select new
            {
                g.HubId,
                Delivery = g.Number,
                Order = order.Number,
                package.Sequence,
                Shop = merchant.Name,
                State = new ParcelState(
                    order.Status,
                    order.CustomerStep == CustomerStep.PayInAdvance && order.ConfirmedOn == null,
                    package.HubId,
                    package.ShuttleToHubId)
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var codes = allHubs.ToDictionary(h => h.Id, h => h.Code);

        return
        [
            .. hubs.Select(hub => new HubNow(
                hub.Code,
                hub.Name,
                open.GetValueOrDefault(hub.Id),
                due.GetValueOrDefault(hub.Id),
                here.GetValueOrDefault(hub.Id),
                onTheWay.GetValueOrDefault(hub.Id),
                riding.GetValueOrDefault(hub.Id),
                riders.GetValueOrDefault(hub.Id),
                [
                    .. away
                        .Where(row => row.HubId == hub.Id && Order.IsForDelivery(row.State.Status))
                        .Select(row => new MissingParcel(
                            new PackageLabel(row.Order, row.Sequence).ToString(),
                            row.Delivery,
                            row.Shop,
                            row.State.PlaceFor(hub.Id)!.Value,
                            row.State.HubId is { } at ? codes.GetValueOrDefault(at) : null))
                ]))
        ];
    }

    private TimeZoneInfo TenantTimeZone()
    {
        var tenant = tenantContext.Tenant ?? throw new InvalidOperationException("The dashboard needs a tenant.");

        return TimeZoneInfo.FindSystemTimeZoneById(tenant.TimeZone);
    }
}
