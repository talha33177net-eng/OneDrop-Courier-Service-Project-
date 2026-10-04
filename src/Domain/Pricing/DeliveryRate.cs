using Domain.Common;

namespace Domain.Pricing;

/// <summary>What a parcel costs the merchant, worked out when it is booked and kept on the parcel.</summary>
/// <param name="CodChargePercent">Taken off the cash collected when the parcel is delivered.</param>
/// <param name="ReturnCharge">Added to the delivery charge when the parcel comes back to the merchant.</param>
public sealed record ParcelCharges(
    ServiceArea ServiceArea,
    decimal DeliveryCharge,
    decimal CodChargePercent,
    decimal ReturnCharge)
{
    /// <summary>The COD charge on <paramref name="collected"/>, rounded to the whole taka.</summary>
    public decimal CodChargeOn(decimal collected)
    {
        return CodCharge(collected, CodChargePercent);
    }

    public static decimal CodCharge(decimal collected, decimal percent)
    {
        return Math.Round(collected * percent / 100m, 0, MidpointRounding.AwayFromZero);
    }
}

/// <summary>
/// One line of a tenant's rate card: what a parcel in one <see cref="ServiceArea"/> costs the merchant. The first
/// <see cref="IncludedWeightGrams"/> cost <see cref="BaseCharge"/>, each started kilogram above it
/// <see cref="ExtraKgCharge"/>. Every number is the tenant's own, set by its admin; none has a default.
/// </summary>
public class DeliveryRate : TenantEntity
{
    public const decimal MaxCharge = 10_000m;
    public const decimal MaxCodChargePercent = 10m;

    private DeliveryRate()
    {
    }

    public ServiceArea ServiceArea { get; private set; }

    public int IncludedWeightGrams { get; private set; }

    public decimal BaseCharge { get; private set; }

    public decimal ExtraKgCharge { get; private set; }

    /// <summary>Per cent of the cash collected at the door.</summary>
    public decimal CodChargePercent { get; private set; }

    /// <summary>Charged on top of the delivery charge for a parcel that comes back to the merchant.</summary>
    public decimal ReturnCharge { get; private set; }

    public static Result<DeliveryRate> Create(ServiceArea area, RateValues values)
    {
        var rate = new DeliveryRate { ServiceArea = area };
        var changed = rate.Change(values);

        return changed.IsSuccess ? rate : changed.Error!;
    }

    /// <summary>New prices for parcels booked from now on. Parcels already booked keep the charges they were booked at.</summary>
    public Result Change(RateValues values)
    {
        if (values.IncludedWeightGrams is < 1 or > 50_000)
        {
            return Error.Validation("rate.weight", "The included weight must be between 1 g and 50 kg.");
        }

        if (values.BaseCharge is < 0 or > MaxCharge || values.ExtraKgCharge is < 0 or > MaxCharge ||
            values.ReturnCharge is < 0 or > MaxCharge)
        {
            return Error.Validation("rate.amount", $"Charges must be between ৳0 and ৳{MaxCharge:N0}.");
        }

        if (values.CodChargePercent is < 0 or > MaxCodChargePercent)
        {
            return Error.Validation("rate.cod", $"The COD charge must be between 0% and {MaxCodChargePercent}%.");
        }

        IncludedWeightGrams = values.IncludedWeightGrams;
        BaseCharge = values.BaseCharge;
        ExtraKgCharge = values.ExtraKgCharge;
        CodChargePercent = values.CodChargePercent;
        ReturnCharge = values.ReturnCharge;

        return Result.Success();
    }

    /// <summary>The delivery charge for a parcel weighing <paramref name="weightGrams"/>.</summary>
    public decimal DeliveryChargeFor(int weightGrams)
    {
        var extraGrams = Math.Max(0, weightGrams - IncludedWeightGrams);
        var extraKg = (extraGrams + 999) / 1000;

        return BaseCharge + extraKg * ExtraKgCharge;
    }

    /// <summary>The charges a parcel weighing <paramref name="weightGrams"/> is booked at.</summary>
    public ParcelCharges ChargesFor(int weightGrams)
    {
        return new ParcelCharges(ServiceArea, DeliveryChargeFor(weightGrams), CodChargePercent, ReturnCharge);
    }
}

public sealed record RateValues(
    int IncludedWeightGrams,
    decimal BaseCharge,
    decimal ExtraKgCharge,
    decimal CodChargePercent,
    decimal ReturnCharge);
