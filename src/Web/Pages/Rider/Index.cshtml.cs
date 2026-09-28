using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Abstractions;
using Application.Delivery.RiderDay;

namespace Web.Pages.Rider;

/// <summary>
/// The rider's day on their phone: today's stops, neighbours together, with the shelf to pick each delivery from, the
/// address and what to collect at the door. Start trip takes out everything ready. Installable as a phone app.
/// </summary>
public class IndexModel(RiderDayHandler handler, ICurrentUser currentUser) : PageModel
{
    /// <summary>Null when the signed-in user is not set up as a rider.</summary>
    public RiderToday? Today { get; private set; }

    [TempData]
    public string? Message { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Today = await handler.TodayAsync(currentUser.UserId!.Value, cancellationToken);
    }

    public async Task<IActionResult> OnPostStartAsync(CancellationToken cancellationToken)
    {
        var started = await handler.StartAsync(currentUser.UserId!.Value, cancellationToken);
        Message = started.IsFailure
            ? started.Error!.Message
            : $"Trip started with {Count(started.Value.Deliveries, "delivery", "deliveries")} and " +
                $"{Count(started.Value.Orders, "order", "orders")}." +
                (started.Value.LeftBehind > 0
                    ? $" {Count(started.Value.LeftBehind, "delivery stays", "deliveries stay")} at the hub: nothing of it was on its shelf."
                    : "");

        return RedirectToPage();
    }

    private static string Count(int count, string one, string many)
    {
        return count == 1 ? $"1 {one}" : $"{count} {many}";
    }
}
