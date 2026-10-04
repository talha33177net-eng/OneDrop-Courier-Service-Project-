using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Parcels.Browse;
using Application.Parcels.ParcelActions;

namespace Web.Pages.Hub;

/// <summary>One parcel for the courier's staff, who can ask for it to go back to its merchant.</summary>
public class ParcelModel(ParcelDetailsHandler details, ParcelActionsHandler actions) : PageModel
{
    public ParcelView Parcel { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(string code, CancellationToken cancellationToken)
    {
        var found = await details.GetAsync(code, cancellationToken);
        if (found.IsFailure)
        {
            return NotFound();
        }

        Parcel = found.Value;

        return Page();
    }

    public async Task<IActionResult> OnPostReturnAsync(string code, string? reason, CancellationToken cancellationToken)
    {
        var returned = await actions.RequestReturnAsync(code, reason, cancellationToken);
        if (returned.IsFailure && returned.Error!.Type == Domain.Common.ErrorType.NotFound)
        {
            return NotFound();
        }

        TempData[returned.IsSuccess ? "Done" : "Problem"] = returned.IsSuccess ? $"{code} is going back to its merchant." : returned.Error!.Message;

        return RedirectToPage(new { code });
    }
}
