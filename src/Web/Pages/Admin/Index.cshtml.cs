using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Dashboards;

namespace Web.Pages.Admin;

/// <summary>The courier admin's home: today's numbers, parcels by stage, every busy hub, the week and the top merchants.</summary>
public class IndexModel(DashboardHandler dashboard) : PageModel
{
    public AdminDashboard Data { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Data = await dashboard.AdminAsync(cancellationToken);
    }
}
