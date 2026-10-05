using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Hubs;
using Application.Hubs.HubOverview;

namespace Web.Pages.Hub;

/// <summary>
/// A page about one hub: the hub named in the request or the one remembered (<see cref="RememberHub"/>); before either,
/// staff are sent to choose one. Every hub page still finds the hub through the courier's own list, so another
/// courier's code is not found. The hub menu shows what waits at each hub.
/// </summary>
public abstract class HubPage(HubOverviewHandler overview) : PageModel
{
    public IReadOnlyList<HubWork> Work { get; private set; } = [];

    public IReadOnlyList<HubItem> Hubs => [.. Work.Select(work => work.Hub)];

    public HubItem? Hub { get; private set; }

    /// <summary>Loads the hubs and picks the current one; false when the code names no hub of this courier.</summary>
    protected async Task<bool> LoadHubAsync(CancellationToken cancellationToken)
    {
        Work = await overview.ListAsync(cancellationToken);
        var code = RememberHub.Of(HttpContext);
        Hub = code is null ? Hubs.FirstOrDefault() : Hubs.FirstOrDefault(h => h.Code == code);

        return Hub is not null || (code is null && Work.Count == 0);
    }
}
