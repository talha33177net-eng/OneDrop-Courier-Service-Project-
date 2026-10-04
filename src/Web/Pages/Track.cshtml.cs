using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Application.Abstractions;
using Application.Parcels.Track;

namespace Web.Pages;

/// <summary>
/// Public parcel tracking on the courier's site: anyone with a tracking code sees the parcel's status and history, never
/// the recipient's details or the money. The platform's own address has no parcels to track.
/// </summary>
[EnableRateLimiting("public")]
public class TrackModel(TrackHandler handler, ITenantContext tenantContext) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Code { get; set; }

    public TrackingView? Parcel { get; private set; }

    public string? Problem { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!tenantContext.HasTenant)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(Code))
        {
            return Page();
        }

        var found = await handler.GetAsync(Code, cancellationToken);
        if (found.IsSuccess)
        {
            Parcel = found.Value;
        }
        else
        {
            Problem = found.Error!.Message;
        }

        return Page();
    }
}
