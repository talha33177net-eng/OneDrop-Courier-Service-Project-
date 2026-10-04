using System.Reflection;
using Domain.Common;
using Domain.Merchants;
using Domain.Network;
using Domain.Parcels;
using Domain.Pricing;

namespace Domain.Tests;

/// <summary>Ready-made domain objects for the tests, with the Dhaka launch rates written out here, not read from code.</summary>
internal static class Build
{
    public const long Mirpur = 1;
    public const long Gulshan = 2;
    public const long Sylhet = 3;

    public static readonly RateValues InsideCity = new(1000, 60, 15, 1, 0);
    public static readonly RateValues Suburb = new(1000, 100, 20, 1, 50);
    public static readonly RateValues OutsideCity = new(1000, 120, 20, 1, 60);

    public static Merchant Merchant(long id = 7)
    {
        var merchant = Domain.Merchants.Merchant.Add(
            new MerchantProfile("Fashion House", "Nusrat Jahan", "01711000001", null, "House 7, Mirpur 10")).Value;

        return WithId(merchant, id);
    }

    public static DeliveryRate Rate(ServiceArea area, RateValues? values = null)
    {
        return DeliveryRate.Create(area, values ?? area switch
        {
            ServiceArea.InsideCity => InsideCity,
            ServiceArea.Suburb => Suburb,
            _ => OutsideCity
        }).Value;
    }

    /// <summary>A booked parcel of merchant 7 with an id, as if saved.</summary>
    public static Parcel Parcel(
        long pickupHub = Mirpur,
        long deliveryHub = Mirpur,
        decimal cod = 1250,
        int grams = 500,
        ServiceArea area = ServiceArea.InsideCity,
        long id = 100)
    {
        var parcel = Domain.Parcels.Parcel.Create(new NewParcel(
            7,
            1,
            pickupHub,
            new ParcelDetails(
                1,
                deliveryHub,
                "Rahim Uddin",
                PhoneNumber.Parse("01811000101").Value,
                "House 22, Road 4",
                cod,
                grams,
                "Panjabi",
                null,
                Rate(area).ChargesFor(grams)))).Value;
        typeof(Parcel).GetProperty(nameof(Domain.Parcels.Parcel.TrackingCode))!.SetValue(parcel, $"OD{10000000 + id}");

        return WithId(parcel, id);
    }

    /// <summary>A parcel out with rider 5, delivered from Mirpur.</summary>
    public static Parcel OutForDelivery(decimal cod = 1250)
    {
        var parcel = Parcel(cod: cod);
        parcel.PickUp();
        parcel.ReceiveAt(Mirpur);
        parcel.AssignTo(5, Mirpur);
        parcel.ClearDomainEvents();

        return parcel;
    }

    public static T WithId<T>(T entity, long id)
        where T : Entity
    {
        typeof(Entity).GetProperty(nameof(Entity.Id), BindingFlags.Public | BindingFlags.Instance)!.SetValue(entity, id);

        return entity;
    }
}
