using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Network.PickupRoutes;

namespace Web.Pages.Hub;

/// <summary>The collector's printable sheet for one route. Another operator's route is a 404.</summary>
public class RouteSheetModel(PickupRoutesHandler handler) : PageModel
{
    public PickupRouteSheet Sheet { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken cancellationToken)
    {
        var sheet = await handler.GetAsync(id, cancellationToken);
        if (sheet is null)
        {
            return NotFound();
        }

        Sheet = sheet;

        return Page();
    }
}
