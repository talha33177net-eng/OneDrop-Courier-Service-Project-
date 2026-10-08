using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Parcels.Stats;
using Domain.Common;

namespace Web.Pages.Merchant;

/// <summary>
/// The merchant's parcels booked in a period it chooses (the last 7 or 30 days, this month, last month or its own
/// days): the delivery, cancel and return rates, where the parcels are now, and count, cash and share per status.
/// </summary>
public class StatsModel(ParcelStatsHandler stats) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "period")]
    public PeriodKind Kind { get; set; } = PeriodKind.Last30Days;

    [BindProperty(SupportsGet = true)]
    public DateOnly? From { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? To { get; set; }

    public ParcelStats? Stats { get; private set; }

    /// <summary>Why the custom days asked for cannot be shown.</summary>
    public string? Problem { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var found = await stats.ForAsync(Kind, From, To, cancellationToken);
        Stats = found.IsSuccess ? found.Value : null;
        Problem = found.Error?.Message;
    }
}
