using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Delivery.Returns;

namespace Web.Pages.Merchant;

/// <summary>The merchant's returns: the parcels coming back and where each is, and the return lists riders bring.</summary>
public class ReturnsModel(MerchantReturnsHandler returns) : PageModel
{
    public MerchantReturns Data { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Data = await returns.GetAsync(cancellationToken);
    }
}
