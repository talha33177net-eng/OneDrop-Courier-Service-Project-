using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Abstractions;
using Application.Common;
using Application.Delivery.Pickups;

namespace Web.Pages.Merchant;

/// <summary>The merchant asks for a rider to collect its parcels at one of its pickup points, and follows its requests.</summary>
public class PickupsModel(PickupsHandler pickups, ITenantContext tenantContext, TimeProvider time) : PageModel
{
    public MerchantPickups Data { get; private set; } = null!;

    public DateOnly Today { get; private set; }

    [BindProperty]
    public long PointId { get; set; }

    [BindProperty]
    public DateOnly Date { get; set; }

    [BindProperty]
    public int Expected { get; set; } = 1;

    [BindProperty]
    public string? Note { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
        Date = Today;
        Expected = Math.Max(1, Data.Points.FirstOrDefault(p => p.IsDefault)?.Waiting ?? 1);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var requested = await pickups.RequestAsync(PointId, Date, Expected, Note, cancellationToken);
        TempData[requested.IsSuccess ? "Done" : "Problem"] = requested.IsSuccess
            ? $"Pickup requested for {Date:dddd d MMMM}. The hub will send a rider."
            : requested.Error!.Message;

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCancelAsync(long id, CancellationToken cancellationToken)
    {
        var cancelled = await pickups.CancelAsync(id, cancellationToken);
        if (cancelled.IsFailure && cancelled.Error!.Type == Domain.Common.ErrorType.NotFound)
        {
            return NotFound();
        }

        TempData[cancelled.IsSuccess ? "Done" : "Problem"] = cancelled.IsSuccess ? "The pickup is cancelled." : cancelled.Error!.Message;

        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Data = await pickups.ForMerchantAsync(cancellationToken);
        Today = tenantContext.Require().Today(time.GetUtcNow().UtcDateTime);
    }
}
