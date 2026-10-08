using Microsoft.AspNetCore.Mvc;
using Application.Abstractions;
using Application.Hubs.HubOverview;
using Application.Hubs.Runs;

namespace Web.Pages.Hub;

/// <summary>
/// The paper a rider takes out with them: every parcel of their run still to hand over, with the door, the phone and
/// the cash to collect, and a line to sign. Another hub's run is not found.
/// </summary>
public class RunSheetModel(HubOverviewHandler hubs, RunsHandler runs, ITenantContext tenantContext) : HubPage(hubs)
{
    public RunSheet? Sheet { get; private set; }

    public string Courier => tenantContext.Tenant?.Name ?? "";

    public string Hotline => tenantContext.Tenant?.SupportPhone ?? "";

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken cancellationToken)
    {
        if (!await LoadHubAsync(cancellationToken) || Hub is null)
        {
            return NotFound();
        }

        Sheet = await runs.SheetAsync(Hub.Code, id, cancellationToken);

        return Sheet is null ? NotFound() : Page();
    }
}
