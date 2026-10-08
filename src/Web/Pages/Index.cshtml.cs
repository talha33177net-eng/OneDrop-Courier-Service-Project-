using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Application.Abstractions;
using Application.Common;
using Application.Pricing.Rates;
using Web.MultiTenancy;

namespace Web.Pages;

/// <summary>
/// The front page. A signed-in user goes to their own panel; a visitor to a courier's site sees what it offers, its
/// prices and a box to track a parcel. A visitor to the bare platform domain is sent to the home courier's site
/// (<see cref="TenancyOptions.HomeCourier"/>), or, with none, sees the list of couriers.
/// </summary>
public class IndexModel(
    ITenantContext tenantContext,
    ITenantCatalog catalog,
    IAppDbContext db,
    RatesHandler rates,
    IOptions<TenancyOptions> tenancy) : PageModel
{
    public TenantInfo? Tenant => tenantContext.Tenant;

    public IReadOnlyList<(TenantInfo Tenant, string Url)> Couriers { get; private set; } = [];

    public IReadOnlyList<RateRow> Rates { get; private set; } = [];

    public int Cities { get; private set; }

    public int Hubs { get; private set; }

    public int Areas { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var home = User.IsInRole(Roles.Merchant) ? "/Merchant/Index"
            : User.IsInRole(Roles.Rider) ? "/Rider/Index"
            : User.IsInRole(Roles.TenantAdmin) ? "/Admin/Index"
            : User.IsInRole(Roles.HubStaff) ? "/Hub/Index"
            : User.IsInRole(Roles.PlatformAdmin) ? "/Platform/Tenants"
            : null;
        if (home is not null)
        {
            return RedirectToPage(home);
        }

        if (Tenant is null)
        {
            if (tenancy.Value.HomeCourier is { Length: > 0 } slug && await catalog.FindBySlugAsync(slug, cancellationToken) is not null)
            {
                return Redirect(tenancy.Value.TenantUrl(Request, slug));
            }

            var tenants = await catalog.ListAsync(cancellationToken);
            Couriers = [.. tenants.Select(t => (t, tenancy.Value.TenantUrl(Request, t.Slug)))];

            return Page();
        }

        Rates = await rates.ListAsync(cancellationToken);
        Cities = await db.Zones.Where(z => !z.Archived).Select(z => z.City).Distinct().CountAsync(cancellationToken);
        Hubs = await db.Hubs.CountAsync(h => !h.Archived, cancellationToken);
        Areas = await db.Areas.CountAsync(a => !a.Archived, cancellationToken);

        return Page();
    }
}
