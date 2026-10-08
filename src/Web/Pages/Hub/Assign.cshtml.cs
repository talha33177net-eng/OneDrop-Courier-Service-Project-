using Microsoft.AspNetCore.Mvc;
using Application.Hubs.HubOverview;
using Application.Hubs.AssignParcels;
using Application.Hubs.HubBoard;

namespace Web.Pages.Hub;

/// <summary>Hub staff tick the parcels waiting for a rider and hand them to one, for today's run.</summary>
public class AssignModel(HubOverviewHandler hubs, HubBoardHandler board, AssignParcelsHandler assign) : HubPage(hubs)
{
    public IReadOnlyList<BoardParcel> Waiting { get; private set; } = [];

    public IReadOnlyList<RiderLoad> Riders { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public string? Area { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!await LoadHubAsync(cancellationToken))
        {
            return NotFound();
        }

        if (Hub is not null)
        {
            Waiting = (await board.GetAsync(Hub.Code, cancellationToken))?.ToAssign ?? [];
            Riders = await assign.RidersAsync(Hub.Code, cancellationToken);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(long riderId, string[] codes, CancellationToken cancellationToken)
    {
        if (!await LoadHubAsync(cancellationToken) || Hub is null)
        {
            return NotFound();
        }

        var assigned = await assign.AssignAsync(Hub.Code, riderId, codes, cancellationToken);
        if (assigned.IsFailure)
        {
            TempData["Problem"] = assigned.Error!.Message;
        }
        else
        {
            // What went is a toast; what stayed behind, and why, stays on the page
            if (assigned.Value.Assigned > 0)
            {
                TempData["Done"] = $"{assigned.Value.Assigned} parcel{(assigned.Value.Assigned == 1 ? "" : "s")} handed over.";
                TempData["DoneLink"] = $"/Hub/RunSheet/{assigned.Value.RunId}?hub={Hub.Code}";
                TempData["DoneLinkText"] = "Print the run sheet";
            }

            if (assigned.Value.Problems.Count > 0)
            {
                TempData["Problem"] = "Not handed over: " + string.Join(" ", assigned.Value.Problems);
            }
        }

        return RedirectToPage(new { hub = Hub.Code });
    }
}
