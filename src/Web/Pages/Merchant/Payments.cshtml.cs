using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Payments.MerchantPayments;

namespace Web.Pages.Merchant;

/// <summary>The merchant's money: what waits for the next payout, line by line, and the payouts already made.</summary>
public class PaymentsModel(MerchantPaymentsHandler payments) : PageModel
{
    public MerchantPayments Data { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Data = await payments.GetAsync(cancellationToken);
    }
}
