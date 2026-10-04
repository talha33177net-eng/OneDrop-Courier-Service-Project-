using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Merchants.Admin;
using Domain.Merchants;

namespace Web.Pages.Admin;

/// <summary>The courier's merchants, by status, with what they send and what they are owed.</summary>
public class MerchantsModel(AdminMerchantsHandler merchants) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public MerchantStatus? Status { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    public IReadOnlyList<MerchantRow> Rows { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Rows = await merchants.ListAsync(Status, Search, cancellationToken);
    }
}
