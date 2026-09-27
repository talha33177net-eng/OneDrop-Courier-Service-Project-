using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Infrastructure.Persistence;

namespace Web.Pages.Platform;

/// <summary>
/// The platform view across tenants. The only page that lifts the tenant filter, and it says so explicitly;
/// it only ever reads counts.
/// </summary>
public class TenantsModel(AppDbContext db) : PageModel
{
    public IReadOnlyList<TenantRow> Tenants { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        string[] acrossTenants = [AppDbContext.TenantFilter];

        Tenants = await db.Tenants
            .OrderBy(t => t.Name)
            .Select(t => new TenantRow(
                t.Name,
                t.Slug,
                t.BaseDeliveryFee,
                t.ExtraShopFee,
                db.Hubs.IgnoreQueryFilters(acrossTenants).Count(h => h.TenantId == t.Id),
                db.Zones.IgnoreQueryFilters(acrossTenants).Count(z => z.TenantId == t.Id),
                db.Merchants.IgnoreQueryFilters(acrossTenants).Count(m => m.TenantId == t.Id),
                db.Customers.IgnoreQueryFilters(acrossTenants).Count(c => c.TenantId == t.Id),
                db.Orders.IgnoreQueryFilters(acrossTenants).Count(o => o.TenantId == t.Id)))
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public sealed record TenantRow(
        string Name,
        string Slug,
        decimal BaseFee,
        decimal ExtraFee,
        int Hubs,
        int Zones,
        int Merchants,
        int Customers,
        int Orders);
}
