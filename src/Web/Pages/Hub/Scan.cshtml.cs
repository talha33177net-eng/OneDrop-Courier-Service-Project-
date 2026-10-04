using Microsoft.AspNetCore.Mvc;
using Application.Hubs;
using Application.Hubs.HubScan;

namespace Web.Pages.Hub;

/// <summary>
/// Hub staff scan labels with a hand scanner (it types into the box and presses Enter) or type the code. Three modes:
/// receive a parcel, send it to another hub, or hand a return back to its merchant. The answer says what to do next.
/// </summary>
public class ScanModel(HubDirectory hubs, HubScanHandler scans) : HubPage(hubs)
{
    [BindProperty(SupportsGet = true)]
    public ScanMode Mode { get; set; }

    [BindProperty]
    public string? Code { get; set; }

    public ScanAnswer? Answer { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        return await LoadHubAsync(cancellationToken) ? Page() : NotFound();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!await LoadHubAsync(cancellationToken) || Hub is null)
        {
            return NotFound();
        }

        var scanned = await scans.ScanAsync(Hub.Code, Mode, Code, cancellationToken);
        if (scanned.IsFailure)
        {
            return NotFound();
        }

        Answer = scanned.Value;
        Code = null;
        ModelState.Clear();

        return Page();
    }
}
