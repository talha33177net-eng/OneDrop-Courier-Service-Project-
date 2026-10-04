using Domain.Network;
using Domain.Pricing;

namespace Domain.Tests;

/// <summary>What a parcel costs: the service area between two zones, the rate by weight, the COD charge.</summary>
public class PricingTests
{
    private static readonly Zone Mirpur = new("MIR", "Mirpur", 1, "Dhaka", isSuburb: false);
    private static readonly Zone Gulshan = new("GUL", "Gulshan", 2, "Dhaka", isSuburb: false);
    private static readonly Zone Savar = new("SAV", "Savar", 3, "Dhaka", isSuburb: true);
    private static readonly Zone Sylhet = new("SYL", "Sylhet", 4, "Sylhet", isSuburb: false);
    private static readonly Zone Agrabad = new("CTG", "Chattogram", 5, "Chattogram", isSuburb: false);

    [Fact]
    public void The_service_area_is_the_same_city_a_suburb_of_it_or_another_city()
    {
        Assert.Equal(ServiceArea.InsideCity, ServiceAreas.Between(Mirpur, Gulshan));
        Assert.Equal(ServiceArea.Suburb, ServiceAreas.Between(Mirpur, Savar));
        Assert.Equal(ServiceArea.InsideCity, ServiceAreas.Between(Savar, Mirpur));
        Assert.Equal(ServiceArea.OutsideCity, ServiceAreas.Between(Mirpur, Sylhet));
        Assert.Equal(ServiceArea.OutsideCity, ServiceAreas.Between(Agrabad, Mirpur));
        Assert.Equal(ServiceArea.InsideCity, ServiceAreas.Between(Agrabad, Agrabad));
    }

    [Theory]
    [InlineData(1, 60)]
    [InlineData(1000, 60)]
    [InlineData(1001, 75)]
    [InlineData(2000, 75)]
    [InlineData(2001, 90)]
    [InlineData(5500, 135)]
    public void Each_started_kilogram_above_the_first_costs_the_extra_charge(int grams, decimal charge)
    {
        Assert.Equal(charge, Build.Rate(ServiceArea.InsideCity).DeliveryChargeFor(grams));
    }

    [Fact]
    public void Each_service_area_has_its_own_price_and_return_charge()
    {
        var outside = Build.Rate(ServiceArea.OutsideCity).ChargesFor(1500);

        Assert.Equal(140, outside.DeliveryCharge);
        Assert.Equal(60, outside.ReturnCharge);
        Assert.Equal(100, Build.Rate(ServiceArea.Suburb).DeliveryChargeFor(800));
    }

    [Theory]
    [InlineData(1250, 1, 13)]
    [InlineData(1249, 1, 12)]
    [InlineData(0, 1, 0)]
    [InlineData(2000, 0.5, 10)]
    [InlineData(1000, 0, 0)]
    public void The_cod_charge_is_a_per_cent_of_the_cash_rounded_to_the_taka(decimal collected, decimal percent, decimal charge)
    {
        Assert.Equal(charge, ParcelCharges.CodCharge(collected, percent));
    }

    [Theory]
    [InlineData(0, 60, 15, 1, 0, "rate.weight")]
    [InlineData(1000, -1, 15, 1, 0, "rate.amount")]
    [InlineData(1000, 60, 15, 11, 0, "rate.cod")]
    [InlineData(1000, 60, 15, 1, -5, "rate.amount")]
    public void A_rate_outside_its_limits_is_refused_and_nothing_changes(int grams, decimal baseCharge, decimal extra, decimal cod, decimal ret, string code)
    {
        var rate = Build.Rate(ServiceArea.InsideCity);

        Assert.Equal(code, rate.Change(new RateValues(grams, baseCharge, extra, cod, ret)).Error?.Code);
        Assert.Equal(60, rate.BaseCharge);
    }
}
