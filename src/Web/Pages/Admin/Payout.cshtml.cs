using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Payments.MerchantPayments;
using Application.Payments.RunPayouts;
using Domain.Common;

namespace Web.Pages.Admin;

/// <summary>Any merchant's payout as an invoice, for the courier's admin; one not yet sent can be sent again or cancelled.</summary>
public class PayoutModel(MerchantPaymentsHandler payments, PayoutsJob job) : PageModel
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

    public async Task<IActionResult> OnPostSendAsync(string number, CancellationToken cancellationToken)
    {
        return Answer(number, await job.SendAgainAsync(number, cancellationToken), $"{number} was sent.");
    }

    public async Task<IActionResult> OnPostCancelAsync(string number, CancellationToken cancellationToken)
    {
        return Answer(number, await job.CancelAsync(number, cancellationToken), $"{number} was cancelled. Its parcels go into the merchant's next payout.");
    }

    private IActionResult Answer(string number, Result result, string done)
    {
        if (result.IsFailure && result.Error!.Type == ErrorType.NotFound)
        {
            return NotFound();
        }

        TempData[result.IsSuccess ? "Done" : "Problem"] = result.IsSuccess ? done : result.Error!.Message;

        return RedirectToPage(new { number });
    }
}
