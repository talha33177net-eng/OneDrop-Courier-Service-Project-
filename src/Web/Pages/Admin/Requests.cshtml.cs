using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Parcels.Requests;
using Domain.Common;

namespace Web.Pages.Admin;

/// <summary>
/// Merchants' requests about parcels on their way, for the courier's admins to approve (the parcel changes there and
/// then) or refuse with a reason the merchant reads; and the ones answered lately.
/// </summary>
public class RequestsModel(ParcelRequestsHandler requests) : PageModel
{
    public IReadOnlyList<RequestRow> Open { get; private set; } = [];

    public IReadOnlyList<RequestRow> Answered { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        (Open, Answered) = await requests.CourierAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostApproveAsync(string code, string? answer, CancellationToken cancellationToken)
    {
        return Answer(await requests.ApproveAsync(code, answer, cancellationToken), $"Approved: {code} is changed.");
    }

    public async Task<IActionResult> OnPostRefuseAsync(string code, string? answer, CancellationToken cancellationToken)
    {
        return Answer(await requests.RefuseAsync(code, answer, cancellationToken), $"Refused: the merchant sees why on {code}.");
    }

    private IActionResult Answer(Result result, string done)
    {
        if (result.IsFailure && result.Error!.Type == ErrorType.NotFound)
        {
            return NotFound();
        }

        TempData[result.IsSuccess ? "Done" : "Problem"] = result.IsSuccess ? done : result.Error!.Message;

        return RedirectToPage();
    }
}
