using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Parcels.Browse;
using Application.Parcels.ParcelActions;
using Application.Parcels.Requests;
using Domain.Parcels;

namespace Web.Pages.Merchant;

/// <summary>
/// One of the merchant's parcels, with what it can still do: edit or cancel it before pickup; once it is on its way, ask
/// the courier to cancel it or to change its cash on delivery, and see the answer.
/// </summary>
public class ParcelModel(ParcelDetailsHandler details, ParcelActionsHandler actions, ParcelRequestsHandler requests) : PageModel
{
    public ParcelView Parcel { get; private set; } = null!;

    /// <summary>The merchant's requests about this parcel, newest first.</summary>
    public IReadOnlyList<RequestRow> Requests { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(string code, CancellationToken cancellationToken)
    {
        var found = await details.GetAsync(code, cancellationToken);
        if (found.IsFailure)
        {
            return NotFound();
        }

        Parcel = found.Value;
        Requests = await requests.ForParcelAsync(code, cancellationToken);

        return Page();
    }

    public async Task<IActionResult> OnPostCancelAsync(string code, string? reason, CancellationToken cancellationToken)
    {
        var cancelled = await actions.CancelAsync(code, reason, cancellationToken);

        return Answer(cancelled, code, $"{code} is cancelled.");
    }

    public async Task<IActionResult> OnPostRequestAsync(
        string code,
        ParcelRequestKind kind,
        decimal? amount,
        string? reason,
        CancellationToken cancellationToken)
    {
        var asked = await requests.AskAsync(code, kind, amount, reason, cancellationToken);

        return Answer(asked, code, $"We asked the courier about {code}. The answer shows here.");
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
