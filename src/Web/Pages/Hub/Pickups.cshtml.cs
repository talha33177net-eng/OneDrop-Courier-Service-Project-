using Microsoft.AspNetCore.Mvc;
using Application.Delivery.Pickups;
using Application.Hubs.HubOverview;
using Application.Hubs.AssignParcels;

namespace Web.Pages.Hub;

/// <summary>The pickups this hub collects: merchants waiting for a rider, and who is going where.</summary>
public class PickupsModel(HubOverviewHandler hubs, PickupsHandler pickups, AssignParcelsHandler riders) : HubPage(hubs)
{
    public IReadOnlyList<PickupRow> Requests { get; private set; } = [];

    public IReadOnlyList<RiderLoad> Riders { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!await LoadHubAsync(cancellationToken))
        {
            return NotFound();
        }

        if (Hub is not null)
        {
            Requests = await pickups.ForHubAsync(Hub.Code, cancellationToken) ?? [];
            Riders = await riders.RidersAsync(Hub.Code, cancellationToken);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAssignAsync(long id, long riderId, CancellationToken cancellationToken)
    {
        if (!await LoadHubAsync(cancellationToken) || Hub is null)
        {
            return NotFound();
        }

        var assigned = await pickups.AssignAsync(Hub.Code, id, riderId, cancellationToken);
        if (assigned.IsFailure && assigned.Error!.Type == Domain.Common.ErrorType.NotFound)
        {
            return NotFound();
        }

        TempData[assigned.IsSuccess ? "Done" : "Problem"] = assigned.IsSuccess ? "Rider assigned to the pickup." : assigned.Error!.Message;

        return RedirectToPage(new { hub = Hub.Code });
    }
}
