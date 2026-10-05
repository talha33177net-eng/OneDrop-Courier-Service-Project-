using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Merchants.Account;
using Application.Merchants.Admin;
using Application.Network.ListAreas;
using Application.Parcels.CreateParcel;
using Application.Parcels.Quote;
using Domain.Pricing;
using Web.Display;

namespace Web.Pages.Merchant;

/// <summary>
/// The merchant books a parcel by hand, through the same handler as the API. Each form carries its own idempotency key,
/// so pressing the button twice books one parcel. The charges are quoted live as the form is filled in.
/// </summary>
public class NewParcelModel(
    CreateParcelHandler handler,
    QuoteHandler quotes,
    ListAreasHandler areas,
    MerchantAccountHandler account) : PageModel
{
    [BindProperty]
    public ParcelForm Input { get; set; } = new();

    [BindProperty]
    public string FormKey { get; set; } = Guid.NewGuid().ToString("N");

    public IReadOnlyList<AreaItem> Areas { get; private set; } = [];

    public IReadOnlyList<PickupPointView> Points { get; private set; } = [];

    public bool CanBook { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(string? next, CancellationToken cancellationToken)
    {
        var booked = await handler.HandleAsync(Input.ToCommand($"form-{FormKey}"), cancellationToken);
        if (booked.IsFailure)
        {
            ModelState.Add(booked.Error!, nameof(Input));
            await LoadAsync(cancellationToken);

            return Page();
        }

        TempData["Done"] = $"Parcel {booked.Value.TrackingCode} is booked for {booked.Value.Area}. Charge ৳{booked.Value.TotalCharge:N0}.";

        return next == "another"
            ? RedirectToPage()
            : RedirectToPage("/Merchant/Parcel", new { code = booked.Value.TrackingCode });
    }

    /// <summary>The live quote beside the form, as JSON.</summary>
    public async Task<IActionResult> OnGetQuoteAsync(
        long? areaId,
        long? pickupPointId,
        decimal weightKg,
        decimal codAmount,
        CancellationToken cancellationToken)
    {
        var quote = await quotes.QuoteAsync(areaId, null, pickupPointId, weightKg, codAmount, cancellationToken);
        if (quote.IsFailure)
        {
            return BadRequest();
        }

        var q = quote.Value;

        return new JsonResult(new
        {
            service = q.ServiceArea.DisplayName(),
            q.DeliveryHub,
            q.DeliveryCharge,
            q.CodCharge,
            q.Total,
            q.ReturnCharge,
            when = q.DeliveryDays is { } days ? Statuses.AfterPickup(days) : null
        });
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Areas = await areas.HandleAsync(cancellationToken);
        var mine = await account.GetAsync(cancellationToken);
        Points = mine.PickupPoints;
        CanBook = mine.Status == Domain.Merchants.MerchantStatus.Active;
    }
}
