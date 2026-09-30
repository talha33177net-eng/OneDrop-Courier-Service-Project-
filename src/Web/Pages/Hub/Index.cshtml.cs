using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Delivery.HubCash;
using Application.Delivery.HubTrips;
using Application.Network.HubScan;
using Application.Network.PickupRoutes;
using Application.Operations.Dashboard;

namespace Web.Pages.Hub;

/// <summary>
/// "Hub today": the hub's day as its six steps in order (pickup, scan, shelves, shuttle, trips, cash), each with what is
/// waiting for it and the button to its page, so staff see where they are and what comes next. Opens on the hub last
/// worked at; another operator's hub is a 404.
/// </summary>
public class IndexModel(
    HubScanHandler scans,
    PickupRoutesHandler routes,
    HubTripsHandler trips,
    HubCashHandler cash,
    OperationsDashboardHandler dashboard) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Hub { get; set; }

    public IReadOnlyList<HubChoice> Hubs { get; private set; } = [];

    /// <summary>Null until staff pick a hub.</summary>
    public HubChoice? Current { get; private set; }

    public IReadOnlyList<PickupRouteSummary> Routes { get; private set; } = [];

    public IReadOnlyList<ShelfRow> Shelves { get; private set; } = [];

    public ShuttleManifest Shuttle { get; private set; } = new([], []);

    public HubTrips Trips { get; private set; } = new(default, [], []);

    public HubCash Cash { get; private set; } = new(default, []);

    public HubNow? Now { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Hubs = await scans.HubsAsync(cancellationToken);
        var code = Hub ?? RememberHub.Of(HttpContext);
        Current = Hubs.FirstOrDefault(hub => string.Equals(hub.Code, code, StringComparison.OrdinalIgnoreCase));
        if (Current is null)
        {
            // A remembered hub that is gone just asks again; a hub named in the address that is not ours is a 404
            return Hub is null ? Page() : NotFound();
        }

        Routes = [.. (await routes.ListAsync(cancellationToken)).Where(route => route.Hub == Current.Name)];
        Shelves = await scans.ShelvesAsync(Current.Code, cancellationToken) ?? [];
        Shuttle = await scans.ShuttleAsync(Current.Code, cancellationToken) ?? Shuttle;
        Trips = await trips.TodayAsync(Current.Code, cancellationToken) ?? Trips;
        Cash = await cash.ListAsync(Current.Code, cancellationToken) ?? Cash;
        Now = await dashboard.HubAsync(Current.Code, cancellationToken);

        return Page();
    }
}
