using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Merchants.Account;
using Application.Network.ListAreas;
using Domain.Common;
using Domain.Merchants;

namespace Web.Pages.Merchant;

/// <summary>The merchant's own settings: business profile, payout account and pickup points.</summary>
public class SettingsModel(MerchantAccountHandler account, ListAreasHandler areas) : PageModel
{
    public MerchantAccount Account { get; private set; } = null!;

    public IReadOnlyList<AreaItem> Areas { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostProfileAsync(
        string? name,
        string? owner,
        string? phone,
        string? email,
        string? address,
        CancellationToken cancellationToken)
    {
        var saved = await account.EditProfileAsync(new MerchantProfile(name, owner, phone, email, address), cancellationToken);

        return Answer(saved, "Your profile is saved.");
    }

    public async Task<IActionResult> OnPostPayoutAsync(
        PayoutMethod method,
        string? payoutAccount,
        string? accountName,
        CancellationToken cancellationToken)
    {
        var saved = await account.SetPayoutAsync(method, payoutAccount, accountName, cancellationToken);

        return Answer(saved, "Your payout account is saved. Payouts go there from now on.");
    }

    public async Task<IActionResult> OnPostPointAsync(
        long? id,
        long areaId,
        string? name,
        string? address,
        string? phone,
        CancellationToken cancellationToken)
    {
        var saved = await account.SavePickupPointAsync(id, areaId, name, address, phone, cancellationToken);

        return Answer(saved, id is null ? "Pickup point added." : "Pickup point saved.");
    }

    public async Task<IActionResult> OnPostDefaultAsync(long id, CancellationToken cancellationToken)
    {
        var saved = await account.MakeDefaultAsync(id, cancellationToken);

        return Answer(saved, "Default pickup point changed.");
    }

    private IActionResult Answer(Result result, string done)
    {
        if (result.IsFailure && result.Error!.Type == ErrorType.NotFound)
        {
            return NotFound();
        }

        TempData[result.IsSuccess ? "Done" : "Problem"] = result.IsSuccess ? done : result.Error!.Message;

        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Account = await account.GetAsync(cancellationToken);
        Areas = await areas.HandleAsync(cancellationToken);
    }
}
