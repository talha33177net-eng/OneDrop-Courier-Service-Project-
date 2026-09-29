using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Network.HubScan;
using Domain.Common;

namespace Web.Pages.Hub;

public enum ScanMode
{
    /// <summary>Hub staff scan parcels in as they arrive at the hub.</summary>
    Receive,

    /// <summary>The collector scans parcels at the shop on the pickup route.</summary>
    Collect,

    /// <summary>Hub staff scan parcels onto the hub shuttle to the hub their delivery leaves from.</summary>
    Load,

    /// <summary>Parcels the customer did not take are handed back to their shop.</summary>
    Return
}

/// <summary>
/// The scanning screen for one hub. A hand scanner types the label and Enter into the box; on a phone the camera
/// reads the QR code. Every scan answers on the same page, with the box ready for the next parcel.
/// </summary>
public class ScanModel(HubScanHandler handler) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Hub { get; set; }

    [BindProperty(SupportsGet = true)]
    public ScanMode Mode { get; set; }

    [BindProperty]
    public string? Label { get; set; }

    public IReadOnlyList<HubChoice> Hubs { get; private set; } = [];

    /// <summary>Null until staff pick the hub they are at.</summary>
    public HubChoice? Current { get; private set; }

    public ParcelScan? Scan { get; private set; }

    public Error? Problem { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        return await LoadHubAsync(cancellationToken) ? Page() : NotFound();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!await LoadHubAsync(cancellationToken) || Current is null)
        {
            return NotFound();
        }

        var result = Mode switch
        {
            ScanMode.Collect => await handler.CollectAsync(Label, cancellationToken),
            ScanMode.Load => await handler.LoadAsync(Current.Code, Label, cancellationToken),
            ScanMode.Return => await handler.ReturnAsync(Label, cancellationToken),
            _ => await handler.ReceiveAsync(Current.Code, Label, cancellationToken)
        };
        Scan = result.IsSuccess ? result.Value : null;
        Problem = result.Error;

        // An empty box for the next parcel
        ModelState.Remove(nameof(Label));
        Label = null;

        return Page();
    }

    /// <summary>False when the page names a hub that is not this operator's.</summary>
    private async Task<bool> LoadHubAsync(CancellationToken cancellationToken)
    {
        Hubs = await handler.HubsAsync(cancellationToken);
        Current = Hubs.FirstOrDefault(hub => string.Equals(hub.Code, Hub, StringComparison.OrdinalIgnoreCase));

        return Hub is null || Current is not null;
    }
}
