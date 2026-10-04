using Microsoft.AspNetCore.Mvc;
using Application.Hubs;
using Application.Hubs.Runs;

namespace Web.Pages.Hub;

/// <summary>Rider closing: count each rider's cash against their deliveries and take back what they could not deliver.</summary>
public class RunsModel(HubDirectory hubs, RunsHandler runs) : HubPage(hubs)
{
    public IReadOnlyList<RunRow> Runs { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!await LoadHubAsync(cancellationToken))
        {
            return NotFound();
        }

        if (Hub is not null)
        {
            Runs = await runs.ListAsync(Hub.Code, cancellationToken) ?? [];
        }

        return Page();
    }

    public async Task<IActionResult> OnPostCloseAsync(long id, decimal received, CancellationToken cancellationToken)
    {
        if (!await LoadHubAsync(cancellationToken) || Hub is null)
        {
            return NotFound();
        }

        var closed = await runs.CloseAsync(Hub.Code, id, received, cancellationToken);
        if (closed.IsFailure && closed.Error!.Type == Domain.Common.ErrorType.NotFound)
        {
            return NotFound();
        }

        TempData[closed.IsSuccess ? "Done" : "Problem"] = closed.IsSuccess
            ? "Run closed. The parcels not delivered are back at the hub."
            : closed.Error!.Message;

        return RedirectToPage(new { hub = Hub.Code });
    }
}
