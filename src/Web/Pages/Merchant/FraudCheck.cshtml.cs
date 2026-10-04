using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Parcels.FraudCheck;

namespace Web.Pages.Merchant;

/// <summary>Before sending cash on delivery, the merchant sees how often a phone number has taken its parcels.</summary>
public class FraudCheckModel(FraudCheckHandler handler) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Phone { get; set; }

    public FraudCheckResult? Result { get; private set; }

    public string? Problem { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Phone))
        {
            return;
        }

        var checkedPhone = await handler.CheckAsync(Phone, cancellationToken);
        if (checkedPhone.IsSuccess)
        {
            Result = checkedPhone.Value;
        }
        else
        {
            Problem = checkedPhone.Error!.Message;
        }
    }
}
