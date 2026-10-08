using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Delivery.Returns;
using Domain.Common;

namespace Web.Pages.Merchant;

/// <summary>
/// One return list: its parcels, and, once the rider has handed it over, the merchant's confirmation that they arrived,
/// with a note when something is missing or damaged. Another merchant's list is not found.
/// </summary>
public class ReturnModel(MerchantReturnsHandler returns) : PageModel
{
    public ReturnListView List { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(string number, CancellationToken cancellationToken)
    {
        var list = await returns.ListAsync(number, cancellationToken);
        if (list is null)
        {
            return NotFound();
        }

        List = list;

        return Page();
    }

    public async Task<IActionResult> OnPostConfirmAsync(string number, string? note, CancellationToken cancellationToken)
    {
        var confirmed = await returns.ConfirmAsync(number, note, cancellationToken);
        if (confirmed.IsFailure && confirmed.Error!.Type == ErrorType.NotFound)
        {
            return NotFound();
        }

        TempData[confirmed.IsSuccess ? "Done" : "Problem"] = confirmed.IsSuccess
            ? $"Thank you. Return list {number.ToUpperInvariant()} is confirmed."
            : confirmed.Error!.Message;

        return RedirectToPage(new { number });
    }
}
