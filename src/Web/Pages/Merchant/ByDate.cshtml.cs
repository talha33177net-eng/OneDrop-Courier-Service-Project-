using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Parcels.Stats;
using Domain.Common;

namespace Web.Pages.Merchant;

/// <summary>The merchant's parcels day by day: how many were booked each day of a period, and where they stand now.</summary>
public class ByDateModel(ParcelStatsHandler stats) : PageModel
{
    [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true, Name = "period")]
    public PeriodKind Kind { get; set; } = PeriodKind.Last30Days;

    [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)]
    public DateOnly? From { get; set; }

    [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)]
    public DateOnly? To { get; set; }

    public ParcelDays? Days { get; private set; }

    /// <summary>Why the custom days asked for cannot be shown.</summary>
    public string? Problem { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var found = await stats.ByDayAsync(Kind, From, To, cancellationToken);
        Days = found.IsSuccess ? found.Value : null;
        Problem = found.Error?.Message;
    }
}
