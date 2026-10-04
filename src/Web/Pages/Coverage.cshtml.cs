using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Abstractions;
using Application.Network.Coverage;

namespace Web.Pages;

/// <summary>The public coverage map: every city and area the courier delivers to, and the hub that serves it.</summary>
public class CoverageModel(CoverageHandler coverage, ITenantContext tenantContext) : PageModel
{
    public IReadOnlyList<CoverageHub> Hubs { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!tenantContext.HasTenant)
        {
            return NotFound();
        }

        Hubs = await coverage.ListAsync(cancellationToken);

        return Page();
    }
}
