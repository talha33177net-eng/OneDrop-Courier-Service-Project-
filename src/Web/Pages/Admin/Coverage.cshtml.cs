using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Network.Coverage;

namespace Web.Pages.Admin;

/// <summary>The coverage map for the admin: hubs, the zones each serves and their areas.</summary>
public class CoverageModel(CoverageHandler coverage) : PageModel
{
    public IReadOnlyList<CoverageHub> Hubs { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Hubs = await coverage.ListAsync(cancellationToken);
    }
}
