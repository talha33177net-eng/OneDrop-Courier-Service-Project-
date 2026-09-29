using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Delivery.HubTrips;
using Application.Network.HubScan;

namespace Web.Pages.Hub;

/// <summary>
/// Today's trips at one hub: each rider's deliveries against their bike's limit, and the deliveries due today that
/// are on no trip. Plan trips now does what the job does every few minutes. Another operator's hub is a 404.
/// </summary>
public class TripsModel(HubScanHandler hubs, HubTripsHandler trips) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Hub { get; set; }

    public IReadOnlyList<HubChoice> Hubs { get; private set; } = [];

    /// <summary>Null until staff pick a hub.</summary>
    public HubChoice? Current { get; private set; }

    public HubTrips Today { get; private set; } = new(default, [], []);

    [TempData]
    public string? Message { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Hubs = await hubs.HubsAsync(cancellationToken);
        if (Hub is null)
        {
            return Page();
        }

        Current = Hubs.FirstOrDefault(hub => string.Equals(hub.Code, Hub, StringComparison.OrdinalIgnoreCase));
        var today = Current is null ? null : await trips.TodayAsync(Current.Code, cancellationToken);
        if (today is null)
        {
            return NotFound();
        }

        Today = today;

        return Page();
    }

    public async Task<IActionResult> OnPostPlanAsync(CancellationToken cancellationToken)
    {
        var planned = Hub is null ? null : await trips.PlanAsync(Hub, cancellationToken);
        if (planned is null)
        {
            return NotFound();
        }

        Message = planned.Planned == 0 && planned.NoRoom == 0
            ? "Nothing new to plan."
            : $"{Deliveries(planned.Planned)} put on a trip." +
                (planned.NoRoom > 0 ? $" {Deliveries(planned.NoRoom)} did not fit on any bike." : "");

        return RedirectToPage(new { hub = Hub });
    }

    private static string Deliveries(int count)
    {
        return count == 1 ? "1 delivery" : $"{count} deliveries";
    }
}
