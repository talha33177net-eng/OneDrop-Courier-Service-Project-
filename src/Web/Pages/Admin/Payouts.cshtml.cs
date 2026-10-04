using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Payments.AdminPayouts;
using Application.Payments.RunPayouts;
using Domain.Payments;

namespace Web.Pages.Admin;

/// <summary>What the courier owes each merchant now, the payouts made, and a button to run the payouts at once.</summary>
public class PayoutsModel(AdminPayoutsHandler payouts, PayoutsJob job) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public PayoutStatus? Status { get; set; }

    public PayoutsOverview Data { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Data = await payouts.GetAsync(Status, cancellationToken);
    }

    public async Task<IActionResult> OnPostRunAsync(CancellationToken cancellationToken)
    {
        var run = await job.PayAsync(cancellationToken);
        TempData["Done"] = $"{run.Paid} payout{(run.Paid == 1 ? "" : "s")} sent, ৳{run.Amount:N0} in all." +
            (run.Waiting > 0 ? $" {run.Waiting} merchant{(run.Waiting == 1 ? "" : "s")} wait: charges more than cash, or no payout account." : "");

        return RedirectToPage();
    }
}
