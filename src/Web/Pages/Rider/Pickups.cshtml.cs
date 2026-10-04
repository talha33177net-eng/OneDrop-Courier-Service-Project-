using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Delivery.RiderDay;

namespace Web.Pages.Rider;

/// <summary>The pickups the hub sent the rider on: tick the parcels collected at each point.</summary>
public class PickupsModel(RiderDayHandler riders) : PageModel
{
    public RiderToday? Today { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Today = await riders.TodayAsync(cancellationToken);

        return Today is null ? Forbid() : Page();
    }

    public async Task<IActionResult> OnPostAsync(long id, string[] codes, CancellationToken cancellationToken)
    {
        var picked = await riders.CompletePickupAsync(id, codes, cancellationToken);
        if (picked.IsFailure && picked.Error!.Type == Domain.Common.ErrorType.NotFound)
        {
            return NotFound();
        }

        TempData[picked.IsSuccess ? "Done" : "Problem"] = picked.IsSuccess
            ? $"Pickup done: {picked.Value} parcel{(picked.Value == 1 ? "" : "s")} collected. Take them to the hub."
            : picked.Error!.Message;

        return RedirectToPage();
    }
}
