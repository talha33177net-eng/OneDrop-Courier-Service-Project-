using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Payments.MerchantPayments;

namespace Web.Pages.Admin;

/// <summary>Any merchant's payout as an invoice, for the courier's admin.</summary>
public class PayoutModel(MerchantPaymentsHandler payments) : PageModel
{
    public PayoutDetails Details { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(string number, CancellationToken cancellationToken)
    {
        var found = await payments.DetailsAsync(number, cancellationToken);
        if (found.IsFailure)
        {
            return NotFound();
        }

        Details = found.Value;

        return Page();
    }
}
