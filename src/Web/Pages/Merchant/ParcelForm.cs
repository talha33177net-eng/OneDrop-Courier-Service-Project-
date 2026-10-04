using Microsoft.AspNetCore.Mvc.ModelBinding;
using Application.Parcels.Browse;
using Application.Parcels.CreateParcel;
using Domain.Common;

namespace Web.Pages.Merchant;

/// <summary>The parcel booking form's fields, as the booking and edit pages bind them.</summary>
public class ParcelForm
{
    public string? MerchantReference { get; set; }

    public string? RecipientName { get; set; }

    public string? RecipientPhone { get; set; }

    public string? RecipientAddress { get; set; }

    public long? AreaId { get; set; }

    public long? PickupPointId { get; set; }

    public decimal CodAmount { get; set; }

    public decimal WeightKg { get; set; } = 0.5m;

    public string? ItemDescription { get; set; }

    public string? Note { get; set; }

    public static ParcelForm From(ParcelView parcel)
    {
        return new ParcelForm
        {
            MerchantReference = parcel.MerchantReference,
            RecipientName = parcel.RecipientName,
            RecipientPhone = Display.Money.Phone(parcel.RecipientPhone),
            RecipientAddress = parcel.RecipientAddress,
            AreaId = parcel.AreaId,
            CodAmount = parcel.CodAmount,
            WeightKg = parcel.WeightGrams / 1000m,
            ItemDescription = parcel.ItemDescription,
            Note = parcel.Note
        };
    }

    public CreateParcelCommand ToCommand(string? idempotencyKey)
    {
        return new CreateParcelCommand
        {
            MerchantReference = MerchantReference,
            RecipientName = RecipientName,
            RecipientPhone = RecipientPhone,
            RecipientAddress = RecipientAddress,
            AreaId = AreaId,
            PickupPointId = PickupPointId,
            CodAmount = CodAmount,
            WeightKg = WeightKg,
            ItemDescription = ItemDescription,
            Note = Note,
            IdempotencyKey = idempotencyKey
        };
    }
}

public static class FormErrors
{
    /// <summary>Puts a business "no" on the form: each field's messages beside its field, anything else at the top.</summary>
    public static void Add(this ModelStateDictionary modelState, Error error, string prefix)
    {
        if (error.Fields is { Count: > 0 } fields)
        {
            foreach (var (field, messages) in fields)
            {
                foreach (var message in messages)
                {
                    modelState.AddModelError($"{prefix}.{field}", message);
                }
            }

            return;
        }

        modelState.AddModelError("", error.Message);
    }
}
