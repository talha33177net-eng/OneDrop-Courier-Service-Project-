using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Network.HubScan;

namespace Web.Pages.Hub;

/// <summary>The deliveries on one hub's shelves and how complete each is. Another operator's hub is a 404.</summary>
public class ShelvesModel(HubScanHandler handler) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Hub { get; set; }

    public IReadOnlyList<HubChoice> Hubs { get; private set; } = [];

    /// <summary>Null until staff pick a hub.</summary>
    public HubChoice? Current { get; private set; }

    public IReadOnlyList<ShelfRow> Shelves { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Hubs = await handler.HubsAsync(cancellationToken);
        if (Hub is null)
        {
            return Page();
        }

        Current = Hubs.FirstOrDefault(hub => string.Equals(hub.Code, Hub, StringComparison.OrdinalIgnoreCase));
        var shelves = Current is null ? null : await handler.ShelvesAsync(Current.Code, cancellationToken);
        if (shelves is null)
        {
            return NotFound();
        }

        Shelves = shelves;

        return Page();
    }
}
