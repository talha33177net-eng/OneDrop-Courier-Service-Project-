using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Delivery.Capacities;
using Domain.Delivery;

namespace Web.Pages.Admin;

/// <summary>What each kind of vehicle carries at once: the hub never hands a rider more, and sends big enough vehicles for pickups.</summary>
public class VehiclesModel(VehicleCapacitiesHandler capacities) : PageModel
{
    public IReadOnlyList<CapacityRow> Rows { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Rows = await capacities.ListAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(
        Vehicle vehicle,
        int maxParcels,
        decimal maxLoadKg,
        decimal maxParcelKg,
        CancellationToken cancellationToken)
    {
        var changed = await capacities.ChangeAsync(
            vehicle,
            new CapacityValues(maxParcels, (int)Math.Round(maxLoadKg * 1000m), (int)Math.Round(maxParcelKg * 1000m)),
            cancellationToken);
        TempData[changed.IsSuccess ? "Done" : "Problem"] = changed.IsSuccess
            ? $"{vehicle.DisplayName()} saved. The next parcels and pickups handed out follow it."
            : changed.Error!.Message;

        return RedirectToPage();
    }
}
