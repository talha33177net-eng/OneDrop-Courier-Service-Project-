using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Infrastructure.Payments;

namespace Web.Pages.Dev;

/// <summary>Development only: the transfers the fake payout gateway sent to merchants.</summary>
public class PayoutsModel(FakePayoutLog payouts, IWebHostEnvironment environment) : PageModel
{
    public IReadOnlyList<FakePayout> Payouts { get; private set; } = [];

    public IActionResult OnGet()
    {
        if (!environment.IsDevelopment())
        {
            return NotFound();
        }

        Payouts = payouts.Recent;

        return Page();
    }
}
