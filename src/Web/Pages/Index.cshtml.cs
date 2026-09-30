using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Application.Abstractions;
using Application.Common;
using Web.MultiTenancy;

namespace Web.Pages;

public class IndexModel(
    ITenantContext tenantContext,
    ITenantCatalog catalog,
    IAppDbContext db,
    IOptions<TenancyOptions> tenancy) : PageModel
{
    public TenantInfo? Tenant => tenantContext.Tenant;

    public IReadOnlyList<(TenantInfo Tenant, string Url)> Tenants { get; private set; } = [];

    public IReadOnlyList<ZoneRow> Zones { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (User.IsInRole(Roles.Merchant))
        {
            return RedirectToPage("/Merchant/Orders");
        }

        if (User.IsInRole(Roles.Customer))
        {
            return RedirectToPage("/Customer/Index");
        }

        if (User.IsInRole(Roles.Rider))
        {
            return RedirectToPage("/Rider/Index");
        }

        if (User.IsInRole(Roles.TenantAdmin))
        {
            return RedirectToPage("/Admin/Index");
        }

        if (User.IsInRole(Roles.HubStaff))
        {
            return RedirectToPage("/Hub/Index");
        }

        if (User.IsInRole(Roles.PlatformAdmin))
        {
            return RedirectToPage("/Platform/Tenants");
        }

        if (Tenant is null)
        {
            var tenants = await catalog.ListAsync(cancellationToken);
            Tenants = [.. tenants.Select(t => (t, tenancy.Value.TenantUrl(Request, t.Slug)))];

            return Page();
        }

        Zones = await db.Zones
            .Where(z => !z.Archived)
            .OrderBy(z => z.Name)
            .Select(z => new ZoneRow(z.Name, z.Hub!.Name, db.Areas.Count(a => a.ZoneId == z.Id)))
            .ToListAsync(cancellationToken);

        return Page();
    }

    public sealed record ZoneRow(string Zone, string Hub, int Areas);
}
