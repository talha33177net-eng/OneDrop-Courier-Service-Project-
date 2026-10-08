using Microsoft.AspNetCore.Mvc;
using Application.Delivery.Returns;
using Application.Hubs.HubOverview;

namespace Web.Pages.Hub;

/// <summary>
/// The returns waiting at the hub that collected them, by the merchant's pickup point they go back to: staff send them
/// with a rider on a return list, and follow the lists until the merchant confirms them.
/// </summary>
public class ReturnsModel(HubOverviewHandler hubs, ReturnListsHandler returns) : HubPage(hubs)
{
    public HubReturns? Data { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!await LoadHubAsync(cancellationToken))
        {
            return NotFound();
        }

        if (Hub is not null)
        {
            Data = await returns.HubAsync(Hub.Code, cancellationToken);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(long riderId, string[] codes, CancellationToken cancellationToken)
    {
        if (!await LoadHubAsync(cancellationToken) || Hub is null)
        {
            return NotFound();
        }

        var sent = await returns.SendAsync(Hub.Code, riderId, codes, cancellationToken);
        TempData[sent.IsSuccess ? "Done" : "Problem"] = sent.IsSuccess
            ? $"Return list {sent.Value} is out with the rider: {codes.Length} parcel{(codes.Length == 1 ? "" : "s")} back to the merchant."
            : sent.Error!.Message;

        return RedirectToPage(new { hub = Hub.Code });
    }
}
