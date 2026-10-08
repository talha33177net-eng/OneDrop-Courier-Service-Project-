using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Delivery.Returns;
using Domain.Common;

namespace Web.Pages.Rider;

/// <summary>
/// At the merchant's door with a return list: the rider records that the parcels were handed over, or why they could
/// not be. Only the rider the list was given to finds it.
/// </summary>
public class ReturnModel(ReturnListsHandler returns) : PageModel
{
    public ReturnListView List { get; private set; } = null!;

    public string? Problem { get; private set; }

    public async Task<IActionResult> OnGetAsync(string number, CancellationToken cancellationToken)
    {
        return await LoadAsync(number, cancellationToken) ? Page() : NotFound();
    }

    public async Task<IActionResult> OnPostHandOverAsync(string number, CancellationToken cancellationToken)
    {
        return await AnswerAsync(number, await returns.HandOverAsync(number, cancellationToken), "Handed back to the merchant", cancellationToken);
    }

    public async Task<IActionResult> OnPostMissAsync(string number, string? reason, CancellationToken cancellationToken)
    {
        return await AnswerAsync(number, await returns.MissAsync(number, reason, cancellationToken), "Not handed over; bring the parcels back to the hub", cancellationToken);
    }

    private async Task<IActionResult> AnswerAsync(string number, Result result, string done, CancellationToken cancellationToken)
    {
        if (result.IsSuccess)
        {
            TempData["Done"] = $"{done}: {number.ToUpperInvariant()}.";

            return RedirectToPage("/Rider/Index");
        }

        if (result.Error!.Type == ErrorType.NotFound || !await LoadAsync(number, cancellationToken))
        {
            return NotFound();
        }

        Problem = result.Error.Message;

        return Page();
    }

    private async Task<bool> LoadAsync(string number, CancellationToken cancellationToken)
    {
        var list = await returns.RiderListAsync(number, cancellationToken);
        if (list is null || list.List.Status != Domain.Delivery.ReturnListStatus.Out)
        {
            return false;
        }

        List = list;

        return true;
    }
}
