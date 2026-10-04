using Application.Abstractions;
using Domain.Common;
using Domain.Pricing;

namespace Application.Parcels.Quote;

/// <summary>What a parcel to <see cref="Area"/> would cost the merchant, before booking it.</summary>
public sealed record QuoteView(
    string Area,
    string DeliveryHub,
    ServiceArea ServiceArea,
    decimal DeliveryCharge,
    decimal CodCharge,
    decimal Total,
    decimal ReturnCharge);

/// <summary>The merchant's price calculator: the same route and rate a booking would get now.</summary>
public class QuoteHandler(ICurrentUser currentUser, ParcelBooking booking)
{
    public async Task<Result<QuoteView>> QuoteAsync(
        long? areaId,
        string? areaName,
        long? pickupPointId,
        decimal weightKg,
        decimal codAmount,
        CancellationToken cancellationToken = default)
    {
        var merchantId = currentUser.MerchantId ?? throw new InvalidOperationException("A merchant asks for a quote.");
        if (weightKg is <= 0 or > Domain.Parcels.Parcel.MaxWeightGrams / 1000m || codAmount < 0)
        {
            return Error.Validation("quote.invalid", "Enter a weight up to 30 kg and a cash amount of ৳0 or more.");
        }

        var route = await booking.ResolveAsync(
            merchantId,
            pickupPointId,
            areaId,
            areaName,
            (int)Math.Round(weightKg * 1000m, MidpointRounding.AwayFromZero),
            cancellationToken);
        if (route.IsFailure)
        {
            return route.Error!;
        }

        var charges = route.Value.Charges;
        var cod = charges.CodChargeOn(codAmount);

        return new QuoteView(
            route.Value.Area.Name,
            route.Value.DeliveryHub.Name,
            charges.ServiceArea,
            charges.DeliveryCharge,
            cod,
            charges.DeliveryCharge + cod,
            charges.ReturnCharge);
    }
}
