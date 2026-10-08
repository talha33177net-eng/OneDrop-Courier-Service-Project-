using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Parcels.Requests;

namespace Web.Pages.Merchant;

/// <summary>The merchant's requests to cancel a parcel on its way or change its cash, and the courier's answers.</summary>
public class RequestsModel(ParcelRequestsHandler requests) : PageModel
{
    public IReadOnlyList<RequestRow> Requests { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Requests = await requests.MerchantAsync(cancellationToken);
    }
}
