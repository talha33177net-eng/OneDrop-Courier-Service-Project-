using Microsoft.EntityFrameworkCore;
using Application.Abstractions;

namespace Application.Merchants;

/// <summary>A shop that brings its parcels to the hub itself until <see cref="Until"/>, after late handovers.</summary>
public sealed record ShopDropOff(long MerchantId, int LateHandovers, DateTime Until);

/// <summary>A hub a shop brings its parcels to, and when its pickup route would have called.</summary>
public sealed record DropOffHub(string Name, string Address, TimeOnly? PickupTime);

/// <summary>
/// What a shop is told on its own pages: it brings its parcels to <see cref="Hubs"/> (the hubs of its pickup points'
/// zones) until <see cref="Until"/>, the tenant's day, after <see cref="LateHandovers"/> in <see cref="WindowDays"/>.
/// </summary>
public sealed record OwnDropOff(int LateHandovers, int WindowDays, DateOnly Until, IReadOnlyList<DropOffHub> Hubs);

/// <summary>
/// The shops that are late too often (<see cref="TenantInfo.DropOff"/>): the pickup route no longer calls on them and
/// they bring their parcels to the hub. Worked out from <c>Order.ShopLateOn</c> when asked, so a shop is back on the
/// route the moment its late handovers leave the window. A merchant's request sees only its own orders, so only its
/// own drop-off.
/// </summary>
public class ShopDropOffs(IAppDbContext db, ITenantContext tenantContext, TimeProvider time)
{
    public async Task<IReadOnlyDictionary<long, ShopDropOff>> CurrentAsync(
        CancellationToken cancellationToken = default)
    {
        var rule = (tenantContext.Tenant ?? throw new InvalidOperationException("Drop-offs need a tenant.")).DropOff;
        var now = time.GetUtcNow().UtcDateTime;
        var windowStart = rule.WindowStart(now);

        var lateHandovers = await db.Orders
            .Where(order => order.ShopLateOn > windowStart)
            .Select(order => new { order.MerchantId, ShopLateOn = order.ShopLateOn!.Value })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return lateHandovers
            .GroupBy(late => late.MerchantId)
            .Select(shop => new
            {
                MerchantId = shop.Key,
                Count = shop.Count(),
                Until = rule.DropsOffUntil(shop.Select(late => late.ShopLateOn), now)
            })
            .Where(shop => shop.Until is not null)
            .ToDictionary(
                shop => shop.MerchantId,
                shop => new ShopDropOff(shop.MerchantId, shop.Count, shop.Until!.Value));
    }

    /// <summary>
    /// The signed-in shop's own drop-off, or null while the route collects from it. Only in a merchant's request: the
    /// merchant filter narrows the orders and pickup points to its own.
    /// </summary>
    public async Task<OwnDropOff?> OwnAsync(CancellationToken cancellationToken = default)
    {
        var dropOff = (await CurrentAsync(cancellationToken)).Values.SingleOrDefault();
        if (dropOff is null)
        {
            return null;
        }

        var hubs = await (
            from point in db.PickupPoints
            join area in db.Areas on point.AreaId equals area.Id
            join zone in db.Zones on area.ZoneId equals zone.Id
            join hub in db.Hubs on zone.HubId equals hub.Id
            join route in db.PickupRoutes.Where(route => !route.Archived) on zone.Id equals route.ZoneId into routes
            from route in routes.DefaultIfEmpty()
            where !point.Archived
            select new DropOffHub(hub.Name, hub.Address, route == null ? null : route.PickupTime))
            .Distinct()
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var tenant = tenantContext.Tenant!;
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(tenant.TimeZone);

        return new OwnDropOff(
            dropOff.LateHandovers,
            tenant.LateHandoverWindowDays,
            DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(dropOff.Until, timeZone)),
            hubs);
    }
}
