using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Delivery.RiderDay;

namespace Web.Pages.Rider;

/// <summary>The rider's deliveries for the day on their phone: what to deliver, what is done, the cash in hand.</summary>
public class IndexModel(RiderDayHandler riders) : PageModel
{
    public RiderToday? Today { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Today = await riders.TodayAsync(cancellationToken);

        return Today is null ? Forbid() : Page();
    }
}
