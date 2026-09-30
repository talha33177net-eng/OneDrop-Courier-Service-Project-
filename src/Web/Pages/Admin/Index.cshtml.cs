using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Abstractions;
using Application.Common;
using Application.Operations.Dashboard;

namespace Web.Pages.Admin;

/// <summary>
/// The operator's dashboard for its admins: every hub now (open deliveries, parcels in, riders out, parcels of today's
/// deliveries not scanned in yet) and packages per delivery by area over the last weeks, the number the business lives
/// on. Live: it reads itself again whenever the operator's data changes.
/// </summary>
public class IndexModel(OperationsDashboardHandler dashboard, ITenantContext tenantContext) : PageModel
{
    /// <summary>How many weeks of packages per delivery the page shows.</summary>
    public const int Weeks = 8;

    public TenantInfo Tenant => tenantContext.Tenant!;

    public OperatorNow Now { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Now = await dashboard.OperatorAsync(Weeks, cancellationToken);
    }
}
