using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Merchants.Admin;
using Application.Network.ListAreas;
using Application.Parcels.Browse;
using Application.Parcels.ParcelActions;

namespace Web.Pages.Merchant;

/// <summary>The merchant corrects a parcel still waiting for pickup; it is priced again at today's rates.</summary>
public class EditParcelModel(ParcelDetailsHandler details, ParcelActionsHandler actions, ListAreasHandler areas) : PageModel
{
    [BindProperty]
    public ParcelForm Input { get; set; } = new();

    public string Code { get; private set; } = "";

    public IReadOnlyList<AreaItem> Areas { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(string code, CancellationToken cancellationToken)
    {
        var found = await details.GetAsync(code, cancellationToken);
        if (found.IsFailure)
        {
            return NotFound();
        }

        if (!found.Value.CanEdit)
        {
            TempData["Problem"] = $"{found.Value.TrackingCode} has been picked up and can no longer be changed.";

            return RedirectToPage("/Merchant/Parcel", new { code = found.Value.TrackingCode });
        }

        Code = found.Value.TrackingCode;
        Input = ParcelForm.From(found.Value);
        Areas = await areas.HandleAsync(cancellationToken);

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string code, CancellationToken cancellationToken)
    {
        var edited = await actions.EditAsync(code, Input.ToCommand(null), cancellationToken);
        if (edited.IsSuccess)
        {
            TempData["Done"] = "The parcel is updated.";

            return RedirectToPage("/Merchant/Parcel", new { code });
        }

        if (edited.Error!.Type == Domain.Common.ErrorType.NotFound)
        {
            return NotFound();
        }

        ModelState.Add(edited.Error, nameof(Input));
        Code = code.ToUpperInvariant();
        Areas = await areas.HandleAsync(cancellationToken);

        return Page();
    }

    public IReadOnlyList<PickupPointView> NoPoints => [];
}
