using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Network.HubScan;

namespace Web.Pages.Hub;

/// <summary>A hub's shuttle manifest: what to load for each other hub, and what is on its way here.</summary>
public class ShuttleModel(HubScanHandler handler) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Hub { get; set; }

    public IReadOnlyList<HubChoice> Hubs { get; private set; } = [];

    /// <summary>Null until staff pick a hub.</summary>
    public HubChoice? Current { get; private set; }

    public ShuttleManifest Manifest { get; private set; } = new([], []);

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Hubs = await handler.HubsAsync(cancellationToken);
        if (Hub is null)
        {
            return Page();
        }

        Current = Hubs.FirstOrDefault(hub => string.Equals(hub.Code, Hub, StringComparison.OrdinalIgnoreCase));
        var manifest = Current is null ? null : await handler.ShuttleAsync(Current.Code, cancellationToken);
        if (manifest is null)
        {
            return NotFound();
        }

        Manifest = manifest;

        return Page();
    }
}
