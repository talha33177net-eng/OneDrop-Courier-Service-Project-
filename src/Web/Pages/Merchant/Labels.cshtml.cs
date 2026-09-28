using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Orders.PackageLabels;

namespace Web.Pages.Merchant;

/// <summary>
/// Printable parcel labels: <c>?order=OD-100001&amp;order=OD-100002</c> for those orders, or every order still
/// waiting for its pickup when none is named.
/// </summary>
public class LabelsModel(PackageLabelsHandler handler) : PageModel
{
    public PackageLabelSheet Sheet { get; private set; } = new([], false);

    public bool Waiting { get; private set; }

    public async Task OnGetAsync(string[] order, CancellationToken cancellationToken)
    {
        Waiting = order.Length == 0;
        Sheet = await handler.HandleAsync(order, cancellationToken);
    }
}
