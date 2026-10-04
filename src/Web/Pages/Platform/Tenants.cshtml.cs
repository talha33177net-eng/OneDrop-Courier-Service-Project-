using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Infrastructure.Persistence;

namespace Web.Pages.Platform;

/// <summary>
/// The platform view across couriers. The only page that lifts the tenant filter, and it says so explicitly, to EF
/// and to the database's Row-Level Security; it only ever reads counts.
/// </summary>
public class TenantsModel(AppDbContext db) : PageModel
{
    public IReadOnlyList<TenantRow> Tenants { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        string[] acrossTenants = [AppDbContext.TenantFilter];

        await using var database = await db.AcrossTenantsAsync(cancellationToken);
        Tenants = await db.Tenants
            .OrderBy(t => t.Name)
            .Select(t => new TenantRow(
                t.Name,
                t.Slug,
                db.Hubs.IgnoreQueryFilters(acrossTenants).Count(h => h.TenantId == t.Id),
                db.Merchants.IgnoreQueryFilters(acrossTenants).Count(m => m.TenantId == t.Id),
                db.Riders.IgnoreQueryFilters(acrossTenants).Count(r => r.TenantId == t.Id),
                db.Parcels.IgnoreQueryFilters(acrossTenants).Count(p => p.TenantId == t.Id)))
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public sealed record TenantRow(string Name, string Slug, int Hubs, int Merchants, int Riders, int Parcels);
}
