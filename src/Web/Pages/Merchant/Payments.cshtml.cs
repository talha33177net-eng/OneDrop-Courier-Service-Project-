using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Abstractions;
using Application.Payments.MerchantPayments;
using Application.Payments.OnlinePayments;
using Application.Payments.RunPayouts;

namespace Web.Pages.Merchant;

/// <summary>
/// The merchant's money: its balance and how it adds up, what is still to collect, the payouts already made, a way to
/// be paid now instead of waiting for the next payout run, and, when its charges come to more than its cash, a way to
/// pay what it owes online. Coming back from the payment page, <c>?payment=PAY-100001</c> says how that payment went.
/// </summary>
public class PaymentsModel(
    MerchantPaymentsHandler payments,
    PayoutsJob payouts,
    OnlinePaymentsHandler online,
    ICurrentUser currentUser) : PageModel
{
    public MerchantPayments Data { get; private set; } = null!;

    public MerchantOnlinePayments Online { get; private set; } = null!;

    /// <summary>The payment the merchant just came back from, as it stands now; null when there is none, or it is another merchant's.</summary>
    public OnlinePaymentRow? Returned { get; private set; }

    public async Task OnGetAsync(string? payment, CancellationToken cancellationToken)
    {
        Returned = string.IsNullOrWhiteSpace(payment) ? null : await online.PaymentAsync(payment, cancellationToken);
        Data = await payments.GetAsync(cancellationToken);
        Online = await online.ForMerchantAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostPayNowAsync(CancellationToken cancellationToken)
    {
        var merchantId = currentUser.MerchantId ?? throw new InvalidOperationException("A merchant is paid.");
        var paid = await payouts.PayNowAsync(merchantId, cancellationToken);
        if (paid.IsFailure)
        {
            TempData["Problem"] = paid.Error!.Message;
        }
        else if (!paid.Value.Sent)
        {
            TempData["Problem"] = $"Payout {paid.Value.Number} was made, but the transfer to your account was refused. We try again every hour; see why on the invoice.";
        }
        else
        {
            TempData["Done"] = $"Payout {paid.Value.Number} is on its way to you.";
            TempData["DoneLink"] = $"/Merchant/Payment/{paid.Value.Number}";
            TempData["DoneLinkText"] = "See the invoice";
        }

        return RedirectToPage();
    }

    /// <summary>Opens a payment of what the merchant owes now and sends it to the gateway's payment page.</summary>
    public async Task<IActionResult> OnPostPayOnlineAsync(CancellationToken cancellationToken)
    {
        var started = await online.StartAsync(cancellationToken);
        if (started.IsFailure)
        {
            TempData["Problem"] = started.Error!.Message;

            return RedirectToPage();
        }

        return Redirect(started.Value.PaymentPageUrl);
    }
}
