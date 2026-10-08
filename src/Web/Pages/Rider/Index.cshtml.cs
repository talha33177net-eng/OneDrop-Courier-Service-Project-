using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Delivery.Returns;
using Application.Delivery.RiderDay;

namespace Web.Pages.Rider;

/// <summary>
/// The rider's day on their phone: what to deliver, the returns to hand back to merchants, what is done, the cash in
/// hand.
/// </summary>
public class IndexModel(RiderDayHandler riders, ReturnListsHandler returns) : PageModel
{
    public RiderToday? Today { get; private set; }

    public IReadOnlyList<ReturnListView> Returns { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Today = await riders.TodayAsync(cancellationToken);
        Returns = await returns.RiderListsAsync(cancellationToken);

        return Today is null ? Forbid() : Page();
    }
}
