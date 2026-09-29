using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Common;
using Application.Grouping.CustomerDeliveries;
using Application.Grouping.ShipNow;

namespace Web.Pages.Customer;

/// <summary>
/// The signed-in customer's deliveries from every shop: what is in each, how many packages have been collected,
/// the fee so far and what travelling together saves, with Ship now on the deliveries still waiting for shops.
/// Installable as a phone app (web manifest).
/// </summary>
public class IndexModel(CustomerDeliveriesHandler deliveries, ShipNowHandler shipNow) : PageModel
{
    public CustomerDeliveries Deliveries { get; private set; } = new([], [], 0);

    [TempData]
    public string? Message { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var customerId = long.Parse(User.FindFirst(AppClaims.CustomerId)!.Value, CultureInfo.InvariantCulture);
        Deliveries = await deliveries.HandleAsync(customerId, cancellationToken);
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
