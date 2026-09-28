using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Network.PickupRoutes;

namespace Web.Pages.Hub;

/// <summary>Every pickup route of the operator, with what is waiting for its next run.</summary>
public class RoutesModel(PickupRoutesHandler handler) : PageModel
{
    public IReadOnlyList<PickupRouteSummary> Routes { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Routes = await handler.ListAsync(cancellationToken);
    }
}
