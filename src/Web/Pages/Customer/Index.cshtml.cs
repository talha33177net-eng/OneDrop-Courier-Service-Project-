using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Common;
using Application.Grouping.CombineDeliveries;
using Application.Grouping.CustomerDeliveries;
using Application.Grouping.ShipNow;

namespace Web.Pages.Customer;

/// <summary>
/// The signed-in customer's deliveries from every shop: what is in each, how many packages have been collected,
/// the fee so far and what travelling together saves, with Ship now on the deliveries still waiting for shops, and
/// "Same address as your delivery …?" when two of them go to addresses spelt differently in one area.
/// Installable as a phone app (web manifest).
/// </summary>
public class IndexModel(
    CustomerDeliveriesHandler deliveries,
    ShipNowHandler shipNow,
    CombineDeliveriesHandler combine) : PageModel
{
    public CustomerDeliveries Deliveries { get; private set; } = new([], [], 0);

    [TempData]
    public string? Message { get; set; }

    private long CustomerId => long.Parse(User.FindFirst(AppClaims.CustomerId)!.Value, CultureInfo.InvariantCulture);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Deliveries = await deliveries.HandleAsync(CustomerId, cancellationToken);
    }

    /// <summary>"Same address": the two deliveries become one.</summary>
    public async Task<IActionResult> OnPostCombineAsync(string number, CancellationToken cancellationToken)
    {
        var combined = await combine.CombineAsync(CustomerId, number, cancellationToken);
        Message = combined.IsSuccess
            ? $"Combined: everything now travels in delivery {combined.Value}, for one fee."
            : combined.Error!.Message;

        return RedirectToPage();
    }

    /// <summary>"Different place": the deliveries stay apart and the address is not asked about again.</summary>
    public async Task<IActionResult> OnPostKeepSeparateAsync(string number, CancellationToken cancellationToken)
    {
        var kept = await combine.KeepSeparateAsync(CustomerId, number, cancellationToken);
        Message = kept.IsSuccess ? "Kept separate: we won't ask about this address again." : kept.Error!.Message;

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostShipNowAsync(string number, CancellationToken cancellationToken)
    {
        var shipped = await shipNow.HandleAsync(number, cancellationToken);
        Message = shipped.IsSuccess
            ? $"Delivery {shipped.Value.Number} is closed. We deliver it on {shipped.Value.DeliveryDate:dddd d MMMM}." +
                (shipped.Value.AddedFee > 0 ? $" Its fee went up by ৳{shipped.Value.AddedFee:N0}." : "")
            : shipped.Error!.Message;

        return RedirectToPage();
    }
}
