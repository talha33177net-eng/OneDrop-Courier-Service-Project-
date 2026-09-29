using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Application.Payments.MerchantPayouts;

namespace Web.Pages.Merchant;

/// <summary>
/// The shop's money: the COD owed to it and any charges, line by line, what goes out in the next payout, and the
/// payouts made. Only the shop's own lines: the merchant filter adds the WHERE.
/// </summary>
public class PayoutsModel(IAppDbContext db, MerchantPayoutsHandler payouts) : PageModel
{
    public string MerchantName { get; private set; } = "";

    public MerchantPayouts Money { get; private set; } = new(0, [], []);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        MerchantName = await db.Merchants.Select(m => m.Name).FirstOrDefaultAsync(cancellationToken) ?? "";
        Money = await payouts.ListAsync(cancellationToken);
    }
}
