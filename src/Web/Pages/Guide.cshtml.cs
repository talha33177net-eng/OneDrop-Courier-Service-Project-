using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Application.Abstractions;
using Web.MultiTenancy;

namespace Web.Pages;

/// <summary>
/// "How it works": one delivery followed from the shop to the payout, step by step, with who does each step and the
/// page to click. In Development each step also names the demo login to use. Open to everyone.
/// </summary>
public class GuideModel(
    ITenantContext tenantContext,
    ITenantCatalog catalog,
    IOptions<TenancyOptions> tenancy,
    IWebHostEnvironment environment) : PageModel
{
    public TenantInfo? Tenant => tenantContext.Tenant;

    public bool Demo => environment.IsDevelopment();

    /// <summary>On the platform's own address: where each operator's portal is.</summary>
    public IReadOnlyList<(TenantInfo Tenant, string Url)> Tenants { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (Tenant is null)
        {
            var tenants = await catalog.ListAsync(cancellationToken);
            Tenants = [.. tenants.Select(tenant => (tenant, tenancy.Value.TenantUrl(Request, tenant.Slug)))];
        }
    }

    /// <summary>A seeded demo login of this operator, e.g. hub@dhaka.onedrop.test.</summary>
    public string Login(string who)
    {
        return $"{who}@{Tenant?.Slug ?? "dhaka"}.onedrop.test";
    }
}
