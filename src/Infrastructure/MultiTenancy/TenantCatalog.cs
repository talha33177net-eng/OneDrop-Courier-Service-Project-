using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Application.Abstractions;
using Infrastructure.Persistence;

namespace Infrastructure.MultiTenancy;

/// <summary>
/// The platform's list of tenants, cached for a few minutes because every request needs it. Platform.Tenant
/// has no tenant filter, so a fresh scope with no tenant set can read it.
/// </summary>
public class TenantCatalog(IServiceScopeFactory scopeFactory, IMemoryCache cache, ILogger<TenantCatalog> logger)
    : ITenantCatalog
{
    private const string CacheKey = "tenants";
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(5);

    public async Task<TenantInfo?> FindBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var tenants = await ListAsync(cancellationToken);

        return tenants.FirstOrDefault(t => t.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<TenantInfo?> FindByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var tenants = await ListAsync(cancellationToken);

        return tenants.FirstOrDefault(t => t.Id == id);
    }

    public async Task<IReadOnlyList<TenantInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        return await cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheFor;
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var tenants = await db.Tenants
                .Where(t => !t.Archived)
                .OrderBy(t => t.Name)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            // A setting added after the launch seed is nullable in the table; a tenant that has not stated it is not
            // served rather than given another operator's value
            foreach (var unset in tenants.Where(t => !IsComplete(t)))
            {
                logger.LogWarning(
                    "Tenant {Slug} has not set every setting added after the launch seed (weight allowance, trust, " +
                        "shop charges or drop-off rule) and is not served",
                    unset.Slug);
            }

            return (IReadOnlyList<TenantInfo>)
            [
                .. tenants
                    .Where(IsComplete)
                    .Select(t => new TenantInfo(
                        t.Id,
                        t.Name,
                        t.Slug,
                        t.TimeZone,
                        t.CurrencyCode,
                        t.SmsSenderName,
                        t.BaseDeliveryFee,
                        t.ExtraShopFee,
                        t.FastDeliveryFee,
                        t.GroupJoinDays,
                        t.WeightAllowanceGrams!.Value,
                        t.ExtraKgFee!.Value,
                        t.TrustedAfterDeliveries!.Value,
                        t.ReturnCharge!.Value,
                        t.LateHandoverFee!.Value,
                        t.TrustedAgainAfterDeliveries!.Value,
                        t.DropOffAfterLateHandovers!.Value,
                        t.LateHandoverWindowDays!.Value))
            ];
        }) ?? [];
    }

    /// <summary>Every setting added after the launch seed, nullable in the table, has been stated.</summary>
    private static bool IsComplete(Domain.Platform.Tenant tenant)
    {
        return tenant.WeightAllowanceGrams is not null &&
            tenant.ExtraKgFee is not null &&
            tenant.TrustedAfterDeliveries is not null &&
            tenant.ReturnCharge is not null &&
            tenant.LateHandoverFee is not null &&
            tenant.TrustedAgainAfterDeliveries is not null &&
            tenant.DropOffAfterLateHandovers is not null &&
            tenant.LateHandoverWindowDays is not null;
    }
}
