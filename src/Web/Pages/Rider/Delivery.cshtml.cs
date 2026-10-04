using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Delivery.RiderDay;
using Domain.Common;

namespace Web.Pages.Rider;

/// <summary>
/// At the door: the rider records what happened to one of their parcels: delivered with the cash collected, partly
/// delivered, held for another day, or refused.
/// </summary>
public class DeliveryModel(RiderDayHandler riders) : PageModel
{
    public RiderDelivery Parcel { get; private set; } = null!;

    public string? Problem { get; private set; }

    public async Task<IActionResult> OnGetAsync(string code, CancellationToken cancellationToken)
    {
        return await LoadAsync(code, cancellationToken) ? Page() : NotFound();
    }

    public async Task<IActionResult> OnPostDeliverAsync(string code, decimal collected, string? reason, CancellationToken cancellationToken)
    {
        return await AnswerAsync(code, await riders.DeliverAsync(code, collected, reason, cancellationToken), "Delivered", cancellationToken);
    }

    public async Task<IActionResult> OnPostHoldAsync(string code, string? reason, DateOnly? until, CancellationToken cancellationToken)
    {
        return await AnswerAsync(code, await riders.HoldAsync(code, reason, until, cancellationToken), "Put on hold", cancellationToken);
    }

    public async Task<IActionResult> OnPostRefuseAsync(string code, string? reason, CancellationToken cancellationToken)
    {
        return await AnswerAsync(code, await riders.RefuseAsync(code, reason, cancellationToken), "Marked refused", cancellationToken);
    }

    private async Task<IActionResult> AnswerAsync(string code, Result result, string done, CancellationToken cancellationToken)
    {
        if (result.IsSuccess)
        {
            TempData["Done"] = $"{done}: {code.ToUpperInvariant()}.";

            return RedirectToPage("/Rider/Index");
        }

        if (result.Error!.Type == ErrorType.NotFound || !await LoadAsync(code, cancellationToken))
        {
            return NotFound();
        }

        Problem = result.Error.Message;

        return Page();
    }

    private async Task<bool> LoadAsync(string code, CancellationToken cancellationToken)
    {
        var today = await riders.TodayAsync(cancellationToken);
        var parcel = today?.Deliveries.FirstOrDefault(d => d.TrackingCode.Equals(code, StringComparison.OrdinalIgnoreCase));
        if (parcel is null)
        {
            return false;
        }

        Parcel = parcel;

        return true;
    }
}
