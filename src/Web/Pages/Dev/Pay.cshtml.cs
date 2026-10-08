using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Infrastructure.Payments;

namespace Web.Pages.Dev;

/// <summary>
/// Development only: the fake gateway's payment page. The payer pays (by bKash or a card, or with the payment marked
/// risky), fails or cancels, and is then posted back to the courier's return address exactly as SSLCommerz posts them:
/// the transaction, its status and, when paid, the validation id the courier checks with the gateway.
/// </summary>
public class PayModel(FakePaymentLog log, IWebHostEnvironment environment) : PageModel
{
    public FakeCharge? Charge { get; private set; }

    /// <summary>Where the page sends the payer back to, and what it posts there; null while the payer has not chosen.</summary>
    public string? BackTo { get; private set; }

    public IReadOnlyDictionary<string, string> BackWith { get; private set; } = new Dictionary<string, string>();

    public IActionResult OnGet(string transactionId)
    {
        if (!environment.IsDevelopment())
        {
            return NotFound();
        }

        Charge = log.Find(transactionId);

        return Charge is null ? NotFound() : Page();
    }

    public IActionResult OnPost(string transactionId, string choice)
    {
        if (!environment.IsDevelopment())
        {
            return NotFound();
        }

        Charge = log.Find(transactionId);
        if (Charge is null)
        {
            return NotFound();
        }

        var session = Charge.Session;
        switch (choice)
        {
            case "bkash" or "card" or "risky":
                var validationId = log.Pay(transactionId, choice == "card" ? "VISA-Test Bank" : "BKASH-BKash", risky: choice == "risky");
                BackTo = session.SuccessUrl;
                BackWith = new Dictionary<string, string> { ["tran_id"] = transactionId, ["val_id"] = validationId!, ["status"] = "VALID" };
                break;
            case "fail":
                log.Fail(transactionId, "The payment failed at the gateway.");
                BackTo = session.FailUrl;
                BackWith = new Dictionary<string, string> { ["tran_id"] = transactionId, ["status"] = "FAILED" };
                break;
            default:
                log.Fail(transactionId, "It was cancelled on the payment page.");
                BackTo = session.CancelUrl;
                BackWith = new Dictionary<string, string> { ["tran_id"] = transactionId, ["status"] = "CANCELLED" };
                break;
        }

        return Page();
    }
}
