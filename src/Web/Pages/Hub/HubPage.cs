using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Hubs;

namespace Web.Pages.Hub;

/// <summary>
/// A page about one hub: the hub named in the request, or the one remembered (<see cref="RememberHub"/>), or the first
/// of the courier's hubs. Every hub page still finds the hub through the courier's own list, so another courier's code
/// is not found.
/// </summary>
public abstract class HubPage(HubDirectory hubs) : PageModel
{
    public IReadOnlyList<HubItem> Hubs { get; private set; } = [];

    public HubItem? Hub { get; private set; }

    /// <summary>Loads the hubs and picks the current one; false when the code names no hub of this courier.</summary>
    protected async Task<bool> LoadHubAsync(CancellationToken cancellationToken)
    {
        Hubs = await hubs.ListAsync(cancellationToken);
        var code = RememberHub.Of(HttpContext);
        Hub = code is null ? Hubs.FirstOrDefault() : Hubs.FirstOrDefault(h => h.Code == code);

        return Hub is not null || (code is null && Hubs.Count == 0);
    }
}
