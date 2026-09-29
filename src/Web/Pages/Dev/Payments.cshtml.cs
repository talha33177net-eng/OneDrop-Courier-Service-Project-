using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Infrastructure.Payments;

namespace Web.Pages.Dev;

/// <summary>
/// Development only: the wallet payments the fake gateway was asked for. Pressing Pay plays the customer paying in
/// the bKash or Nagad app, so the rider's "Check payment" goes through. Below, the payouts the fake payout gateway sent
/// to shops.
/// </summary>
public class PaymentsModel(FakePaymentLog log, FakePayoutLog payouts, TimeProvider time, IWebHostEnvironment environment)
    : PageModel
{
    public IReadOnlyList<FakePaymentRequest> Requests { get; private set; } = [];

    public IReadOnlyList<FakePayout> Payouts { get; private set; } = [];

    public IActionResult OnGet()
    {
        if (!environment.IsDevelopment())
        {
            return NotFound();
        }

        Requests = log.Recent;
        Payouts = payouts.Recent;

        return Page();
    }

    public IActionResult OnPostPay(string reference)
    {
        if (!environment.IsDevelopment())
        {
            return NotFound();
        }

        log.Pay(reference, time.GetUtcNow().UtcDateTime);

        return RedirectToPage();
    }
}
