using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Domain.Common;
using Infrastructure.Payments;

namespace Web.Pages.Dev;

/// <summary>
/// Development only: the transfers the fake payout gateway sent to merchants, and accounts it can be told to refuse so
/// a stuck payout can be seen on the admin's page.
/// </summary>
public class PayoutsModel(FakePayoutLog payouts, IWebHostEnvironment environment) : PageModel
{
    public IReadOnlyList<FakePayout> Payouts { get; private set; } = [];

    public IReadOnlyDictionary<string, string> Refused { get; private set; } = new Dictionary<string, string>();

    public IActionResult OnGet()
    {
        if (!environment.IsDevelopment())
        {
            return NotFound();
        }

        Payouts = payouts.Recent;
        Refused = payouts.Refused;

        return Page();
    }

    public IActionResult OnPostRefuse(string? account, string? reason)
    {
        if (!environment.IsDevelopment())
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(account))
        {
            TempData["Problem"] = "Enter the account to refuse.";

            return RedirectToPage();
        }

        // Mobile accounts are kept as +8801…, as the merchant's payout account is
        var kept = PhoneNumber.Parse(account) is { IsSuccess: true } phone ? phone.Value.Value : account.Trim();
        payouts.Refuse(kept, string.IsNullOrWhiteSpace(reason) ? "The receiving account was not found." : reason.Trim());
        TempData["Done"] = $"Transfers to {kept} are refused until you stop it.";

        return RedirectToPage();
    }

    public IActionResult OnPostAllow(string? account)
    {
        if (!environment.IsDevelopment())
        {
            return NotFound();
        }

        payouts.StopRefusing(account ?? "");
        TempData["Done"] = $"Transfers to {account} go through again.";

        return RedirectToPage();
    }
}
