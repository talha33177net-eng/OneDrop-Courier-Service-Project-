using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Delivery.HubCash;
using Application.Network.HubScan;

namespace Web.Pages.Hub;

/// <summary>
/// The riders' cash at one hub: what each rider collected in cash on today's trip (and earlier trips not handed in
/// yet), and a box to record what they handed in once every stop is done. Another operator's hub is a 404.
/// </summary>
public class CashModel(HubScanHandler hubs, HubCashHandler cash) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Hub { get; set; }

    public IReadOnlyList<HubChoice> Hubs { get; private set; } = [];

    /// <summary>Null until staff pick a hub.</summary>
    public HubChoice? Current { get; private set; }

    public HubCash Today { get; private set; } = new(default, []);

    [TempData]
    public string? Message { get; set; }

    [TempData]
    public string? Problem { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Hubs = await hubs.HubsAsync(cancellationToken);
        if (Hub is null)
        {
            return Page();
        }

        Current = Hubs.FirstOrDefault(hub => string.Equals(hub.Code, Hub, StringComparison.OrdinalIgnoreCase));
        var today = Current is null ? null : await cash.ListAsync(Current.Code, cancellationToken);
        if (today is null)
        {
            return NotFound();
        }

        Today = today;

        return Page();
    }

    public async Task<IActionResult> OnPostHandInAsync(long trip, decimal? received, CancellationToken cancellationToken)
    {
        if (Hub is null)
        {
            return NotFound();
        }

        if (received is null)
        {
            Problem = "Enter the cash you counted.";

            return RedirectToPage(new { hub = Hub });
        }

        var handedIn = await cash.HandInAsync(Hub, trip, received.Value, cancellationToken);
        if (handedIn.IsFailure && handedIn.Error!.Code == "trip.cash.none")
        {
            return NotFound();
        }

        if (handedIn.IsFailure)
        {
            Problem = handedIn.Error!.Message;
        }
        else
        {
            var row = handedIn.Value;
            Message = row.Short switch
            {
                > 0 => $"{row.Rider} handed in ৳{row.HandedIn:N0}, ৳{row.Short:N0} short of the ৳{row.Cash:N0} collected.",
                < 0 => $"{row.Rider} handed in ৳{row.HandedIn:N0}, ৳{-row.Short:N0} more than the ৳{row.Cash:N0} collected.",
                _ => $"{row.Rider} handed in ৳{row.HandedIn:N0}, all the cash collected."
            };
        }

        return RedirectToPage(new { hub = Hub });
    }
}
