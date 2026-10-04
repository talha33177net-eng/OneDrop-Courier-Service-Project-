using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Dashboards;

namespace Web.Pages.Merchant;

/// <summary>The merchant's home: its parcels by stage, the money waiting for it, the week, and the latest parcels.</summary>
public class IndexModel(DashboardHandler dashboard) : PageModel
{
    public MerchantDashboard Data { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Data = await dashboard.MerchantAsync(cancellationToken);
    }
}
