using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Parcels.Browse;
using Application.Parcels.ParcelActions;

namespace Web.Pages.Merchant;

/// <summary>One of the merchant's parcels, with what it can still do: edit or cancel before pickup, ask for it back later.</summary>
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

    public async Task<IActionResult> OnPostCancelAsync(string code, string? reason, CancellationToken cancellationToken)
    {
        var cancelled = await actions.CancelAsync(code, reason, cancellationToken);

        return Answer(cancelled, code, $"{code} is cancelled.");
    }

    public async Task<IActionResult> OnPostReturnAsync(string code, string? reason, CancellationToken cancellationToken)
    {
        var returned = await actions.RequestReturnAsync(code, reason, cancellationToken);

        return Answer(returned, code, $"We will bring {code} back to you.");
    }

    private IActionResult Answer(Domain.Common.Result result, string code, string done)
    {
        if (result.IsFailure && result.Error!.Type == Domain.Common.ErrorType.NotFound)
        {
            return NotFound();
        }

        TempData[result.IsSuccess ? "Done" : "Problem"] = result.IsSuccess ? done : result.Error!.Message;

        return RedirectToPage(new { code });
    }
}
