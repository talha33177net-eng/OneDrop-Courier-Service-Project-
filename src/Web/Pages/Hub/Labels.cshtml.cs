using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Abstractions;
using Application.Parcels.Labels;

namespace Web.Pages.Hub;

/// <summary>Hub staff reprint the labels of the parcels named, for one that lost or damaged its own.</summary>
public class LabelsModel(ParcelLabelsHandler labels, ITenantContext tenantContext) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string[] Codes { get; set; } = [];

    public IReadOnlyList<ParcelLabel> Labels { get; private set; } = [];

    public string Courier => tenantContext.Tenant?.Name ?? "";

    public string Hotline => tenantContext.Tenant?.SupportPhone ?? "";

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Labels = Codes.Length == 0 ? [] : await labels.ListAsync(Codes, cancellationToken);
    }
}
