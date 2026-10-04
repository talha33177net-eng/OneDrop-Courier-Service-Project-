using Microsoft.AspNetCore.Mvc;
using Application.Hubs;
using Application.Hubs.HubBoard;

namespace Web.Pages.Hub;

/// <summary>The hub's day at a glance: what comes in, what waits for a rider, what to send on and what to hand back.</summary>
public class IndexModel(HubDirectory hubs, HubBoardHandler board) : HubPage(hubs)
{
    public HubBoard? Board { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!await LoadHubAsync(cancellationToken))
        {
            return NotFound();
        }

        if (Hub is not null)
        {
            Board = await board.GetAsync(Hub.Code, cancellationToken);
        }

        return Page();
    }
}
