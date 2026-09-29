using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Abstractions;
using Application.Delivery.Door;
using Application.Delivery.RiderDay;
using Domain.Delivery;
using Domain.Payments;

namespace Web.Pages.Rider;

/// <summary>
/// The rider's day on their phone: today's stops, neighbours together, with the shelf to pick each delivery from, the
/// address and what to collect at the door. Start trip takes out everything ready. At a door the rider ticks what the
/// customer refuses, checks the amount (a plain link, so a mistaken tap changes nothing) and hands over once paid: in
/// cash, or by bKash or Nagad through a QR on the rider's phone. Or records that nobody was home. Installable as a
/// phone app.
/// </summary>
public class IndexModel(RiderDayHandler handler, DoorHandler door, ICurrentUser currentUser) : PageModel
{
    /// <summary>Null when the signed-in user is not set up as a rider.</summary>
    public RiderToday? Today { get; private set; }

    /// <summary>The amount to collect at the stop the rider is checking, with the orders the customer refuses.</summary>
    public DoorDue? Due { get; private set; }

    [TempData]
    public string? Message { get; set; }

    public string? Problem { get; private set; }

    public async Task OnGetAsync(string? stop, string[]? refused, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId!.Value;
        if (stop is not null)
        {
            var due = await door.DueAsync(userId, stop, refused ?? [], cancellationToken);
            Due = due.IsSuccess ? due.Value : null;
            Problem = due.Error?.Message;
        }

        Today = await handler.TodayAsync(userId, cancellationToken);
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
                    : "") +
                (started.Value.OrdersFollowing > 0
                    ? " " + Count(
                        started.Value.OrdersFollowing,
                        "order was not ready and follows",
                        "orders were not ready and follow") + " in a later delivery."
                    : "");

        return RedirectToPage();
    }

    /// <summary>Shows a bKash or Nagad QR for the amount due; the customer scans it with their wallet app.</summary>
    public async Task<IActionResult> OnPostQrAsync(
        string stop,
        string[]? refused,
        decimal collected,
        PaymentMethod method,
        CancellationToken cancellationToken)
    {
        var qr = await door.RequestQrAsync(currentUser.UserId!.Value, stop, refused ?? [], method, collected, cancellationToken);
        Message = qr.Error?.Message;

        return RedirectToPage(null, null, new { stop, refused }, "door");
    }

    /// <summary>
    /// Hands over once paid: cash as the rider confirms it, a wallet once the gateway has the money. When it cannot,
    /// the rider stays at the door with the reason.
    /// </summary>
    public async Task<IActionResult> OnPostHandOverAsync(
        string stop,
        string[]? refused,
        decimal collected,
        PaymentMethod method,
        CancellationToken cancellationToken)
    {
        var done = await door.HandOverAsync(
            currentUser.UserId!.Value,
            stop,
            refused ?? [],
            collected,
            method,
            cancellationToken);
        if (done.IsFailure)
        {
            Message = done.Error!.Message;

            return RedirectToPage(null, null, new { stop, refused }, "door");
        }

        Message = done.Value.Outcome == StopOutcome.Delivered
            ? $"Handed over. Collected ৳{done.Value.Collected:N0} " +
                (done.Value.Method == PaymentMethod.Cash ? "in cash." : $"by {done.Value.Method!.Value.DisplayName()}.")
            : "Everything refused: bring the parcels back to the hub for their shops.";

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostNotHomeAsync(string stop, CancellationToken cancellationToken)
    {
        var done = await door.NotHomeAsync(currentUser.UserId!.Value, stop, cancellationToken);
        Message = done.IsFailure
            ? done.Error!.Message
            : done.Value.BackToShop
                ? "Nobody home again: bring the parcels back to the hub for their shops."
                : "Nobody home: bring the parcels back to the hub. We try again on another day, free.";

        return RedirectToPage();
    }

    private static string Count(int count, string one, string many)
    {
        return count == 1 ? $"1 {one}" : $"{count} {many}";
    }
}
