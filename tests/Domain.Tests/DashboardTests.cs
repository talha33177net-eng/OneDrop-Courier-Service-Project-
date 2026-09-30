using Domain.Grouping;
using Domain.Orders;

namespace Domain.Tests;

/// <summary>Task 4.1: where a parcel of today's delivery is, and packages per delivery week by week.</summary>
public class DashboardTests
{
    private const long Mirpur = 3;
    private const long Gulshan = 5;

    [Fact]
    public void A_parcel_on_its_deliverys_shelf_is_not_missing()
    {
        Assert.Null(new ParcelState(OrderStatus.AtHub, false, Mirpur, null).PlaceFor(Mirpur));
        Assert.Null(new ParcelState(OrderStatus.PickedUp, false, Mirpur, null).PlaceFor(Mirpur));
    }

    [Fact]
    public void A_parcel_not_here_is_placed_by_its_last_scan_then_by_its_orders_status()
    {
        Assert.Equal(ParcelPlace.AtTheShop, new ParcelState(OrderStatus.Created, false, null, null).PlaceFor(Mirpur));
        Assert.Equal(ParcelPlace.WaitingForAdvance, new ParcelState(OrderStatus.Created, true, null, null).PlaceFor(Mirpur));
        Assert.Equal(ParcelPlace.WithTheCollector, new ParcelState(OrderStatus.PickedUp, false, null, null).PlaceFor(Mirpur));
        Assert.Equal(ParcelPlace.AtAnotherHub, new ParcelState(OrderStatus.AtHub, false, Gulshan, null).PlaceFor(Mirpur));
        Assert.Equal(ParcelPlace.OnTheShuttle, new ParcelState(OrderStatus.AtHub, false, null, Mirpur).PlaceFor(Mirpur));

        // Nobody home: the order is back at the hub on paper, the parcel still with the rider until scanned in
        Assert.Equal(ParcelPlace.WithARider, new ParcelState(OrderStatus.AtHub, false, null, null).PlaceFor(Mirpur));
    }

    [Fact]
    public void Packages_per_delivery_is_the_packages_over_the_deliveries_to_one_decimal_and_nothing_without_deliveries()
    {
        Assert.Null(DeliveryDensity.None.PackagesPerDelivery);
        Assert.Equal(1.0m, new DeliveryDensity(1, 1).PackagesPerDelivery);
        Assert.Equal(2.5m, new DeliveryDensity(2, 5).PackagesPerDelivery);
        Assert.Equal(1.7m, new DeliveryDensity(3, 5).PackagesPerDelivery);
        Assert.Equal(1.3m, new DeliveryDensity(4, 5).PackagesPerDelivery);
        Assert.Equal(new DeliveryDensity(3, 7), new DeliveryDensity(1, 3) + new DeliveryDensity(2, 4));
    }

    [Fact]
    public void A_week_is_seven_days_ending_today_counted_back_whole()
    {
        var today = new DateOnly(2026, 9, 30);

        Assert.Equal(0, DeliveryDensity.WeeksBack(today, today));
        Assert.Equal(0, DeliveryDensity.WeeksBack(today, new DateOnly(2026, 9, 24)));
        Assert.Equal(1, DeliveryDensity.WeeksBack(today, new DateOnly(2026, 9, 23)));
        Assert.Equal(1, DeliveryDensity.WeeksBack(today, new DateOnly(2026, 9, 17)));
        Assert.Equal(7, DeliveryDensity.WeeksBack(today, new DateOnly(2026, 8, 6)));
    }
}
