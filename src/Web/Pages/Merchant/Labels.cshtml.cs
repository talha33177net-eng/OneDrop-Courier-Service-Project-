using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Abstractions;
using Application.Parcels.Labels;

namespace Web.Pages.Merchant;

/// <summary>Printable labels for the parcels named, or every parcel still waiting for pickup. Only the merchant's own.</summary>
public class LabelsModel(ParcelLabelsHandler labels, ITenantContext tenantContext) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string[] Codes { get; set; } = [];

    public IReadOnlyList<ParcelLabel> Labels { get; private set; } = [];

    public string Courier => tenantContext.Tenant?.Name ?? "";

    public string Hotline => tenantContext.Tenant?.SupportPhone ?? "";

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Labels = await labels.ListAsync(Codes, cancellationToken);
    }
}
