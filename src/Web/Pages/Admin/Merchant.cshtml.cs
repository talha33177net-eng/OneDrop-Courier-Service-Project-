using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Merchants.Admin;
using Application.Parcels.Browse;
using Domain.Common;
using Domain.Merchants;

namespace Web.Pages.Admin;

/// <summary>One merchant for the admin: approve, suspend or reactivate, correct its profile and payout account.</summary>
public class MerchantModel(AdminMerchantsHandler merchants, ParcelListHandler parcels) : PageModel
{
    public MerchantView Merchant { get; private set; } = null!;

    public IReadOnlyList<ParcelRow> Recent { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken cancellationToken)
    {
        var found = await merchants.GetAsync(id, cancellationToken);
        if (found.IsFailure)
        {
            return NotFound();
        }

        Merchant = found.Value;
        Recent = (await parcels.ListAsync(new ParcelQuery { MerchantId = id, PageSize = 10 }, cancellationToken)).Page.Items;

        return Page();
    }

    public async Task<IActionResult> OnPostApproveAsync(long id, CancellationToken cancellationToken)
    {
        return Answer(id, await merchants.ApproveAsync(id, cancellationToken), "Approved: the merchant can book parcels now.");
    }

    public async Task<IActionResult> OnPostSuspendAsync(long id, CancellationToken cancellationToken)
    {
        return Answer(id, await merchants.SuspendAsync(id, cancellationToken), "Suspended: the merchant cannot book parcels.");
    }

    public async Task<IActionResult> OnPostReactivateAsync(long id, CancellationToken cancellationToken)
    {
        return Answer(id, await merchants.ReactivateAsync(id, cancellationToken), "Reactivated.");
    }

    public async Task<IActionResult> OnPostProfileAsync(
        long id,
        string? name,
        string? owner,
        string? phone,
        string? email,
        string? address,
        CancellationToken cancellationToken)
    {
        var saved = await merchants.EditAsync(id, new MerchantProfile(name, owner, phone, email, address), cancellationToken);

        return Answer(id, saved, "Profile saved.");
    }

    public async Task<IActionResult> OnPostPayoutAsync(
        long id,
        PayoutMethod method,
        string? payoutAccount,
        string? accountName,
        CancellationToken cancellationToken)
    {
        return Answer(id, await merchants.SetPayoutAsync(id, method, payoutAccount, accountName, cancellationToken), "Payout account saved.");
    }

    private IActionResult Answer(long id, Result result, string done)
    {
        if (result.IsFailure && result.Error!.Type == ErrorType.NotFound)
        {
            return NotFound();
        }

        TempData[result.IsSuccess ? "Done" : "Problem"] = result.IsSuccess ? done : result.Error!.Message;

        return RedirectToPage(new { id });
    }
}
