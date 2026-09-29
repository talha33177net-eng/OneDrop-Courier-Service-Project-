using Domain.Common;
using Domain.Customers;
using Domain.Delivery;
using Domain.Grouping;
using Domain.Orders;

namespace Domain.Tests;

/// <summary>Task 3.4: riders' bikes, trips and the plan that fills them.</summary>
public class TripTests
{
    private const long Mirpur = 1;
    private const long Gulshan = 2;

    private static readonly DateTime Now = new(2026, 9, 28, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 9, 30);

    [Fact]
    public void A_load_fits_up_to_the_limit_on_both_counts()
    {
        var limit = new TripLoad(10, 20_000);

        Assert.True(new TripLoad(10, 20_000).FitsIn(limit));
        Assert.False(new TripLoad(11, 1_000).FitsIn(limit));
        Assert.False(new TripLoad(1, 20_001).FitsIn(limit));
        Assert.Equal(new TripLoad(5, 3_500), new TripLoad(2, 1_000) + new TripLoad(3, 2_500));
    }

    [Theory]
    [InlineData(0, 1_000)]
    [InlineData(10, 0)]
    public void A_bike_carries_at_least_one_parcel_and_some_weight(int parcels, int grams)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Rider(Mirpur, "Rafiq", Phone(), new TripLoad(parcels, grams), null));
    }

    [Fact]
    public void Deliveries_fill_the_first_bike_then_the_next_and_the_rest_wait()
    {
        Bike[] bikes = [Bike(1, parcels: 3), Bike(2, parcels: 2)];

        var plan = TripPlanner.Plan(bikes, [Waiting(10, "Mirpur 10", 2), Waiting(11, "Mirpur 10", 1), Waiting(12, "Mirpur 11", 2), Waiting(13, "Pallabi", 1)]);

        Assert.Equal([new(10, 1), new(11, 1), new(12, 2)], plan.Assignments);
        Assert.Equal([13L], plan.NoRoom);
    }

    [Fact]
    public void A_heavy_delivery_goes_to_a_bike_with_the_weight_to_spare()
    {
        Bike[] bikes = [Bike(1, parcels: 30, grams: 5_000), Bike(2, parcels: 30, grams: 25_000)];

        var plan = TripPlanner.Plan(bikes, [Waiting(10, "Mirpur 10", 1, grams: 12_000), Waiting(11, "Mirpur 10", 1, grams: 1_000)]);

        Assert.Equal([new(10, 2), new(11, 1)], plan.Assignments);
    }

    [Fact]
    public void What_a_bike_already_carries_counts_against_its_limit()
    {
        Bike[] bikes = [new(1, new TripLoad(5, 25_000), new TripLoad(4, 2_000)), Bike(2, parcels: 5)];

        var plan = TripPlanner.Plan(bikes, [Waiting(10, "Mirpur 10", 2), Waiting(11, "Mirpur 10", 1)]);

        Assert.Equal([new(10, 2), new(11, 1)], plan.Assignments);
    }

    [Fact]
    public void A_delivery_too_big_for_any_bike_waits_and_smaller_ones_still_go()
    {
        var plan = TripPlanner.Plan([Bike(1, parcels: 4)], [Waiting(10, "Mirpur 10", 5), Waiting(11, "Mirpur 10", 4)]);

        Assert.Equal([new(11, 1)], plan.Assignments);
        Assert.Equal([10L], plan.NoRoom);
    }

    [Fact]
    public void Late_deliveries_go_first_then_neighbours_together()
    {
        Bike[] bikes = [Bike(1, parcels: 2), Bike(2, parcels: 2)];
        WaitingDelivery[] waiting =
        [
            Waiting(10, "Pallabi", 1),
            Waiting(11, "Mirpur 10", 1),
            Waiting(12, "Pallabi", 1),
            Waiting(13, "Mirpur 10", 1),
            Waiting(14, "Uttara", 1) with { DueOn = Today.AddDays(-1) }
        ];

        var plan = TripPlanner.Plan(bikes, waiting);

        Assert.Equal([new(14, 1), new(11, 1), new(13, 2), new(10, 2)], plan.Assignments);
        Assert.Equal([12L], plan.NoRoom);
    }

    [Fact]
    public void Without_bikes_nothing_is_planned()
    {
        var plan = TripPlanner.Plan([], [Waiting(10, "Mirpur 10", 1)]);

        Assert.Empty(plan.Assignments);
        Assert.Equal([10L], plan.NoRoom);
    }

    [Fact]
    public void A_planned_trip_takes_a_closed_delivery_of_its_hub_on_its_day()
    {
        var trip = NewTrip();
        var group = Locked(Mirpur);

        var stop = trip.Add(group);

        Assert.Equal((trip.Id, group.Id, Today), (stop.Value.TripId, stop.Value.DeliveryGroupId, stop.Value.DeliveryDate));
        Assert.Equal((TripStatus.Planned, Mirpur), (trip.Status, trip.HubId));
    }

    [Fact]
    public void An_open_delivery_and_a_trip_that_left_take_no_stop_and_another_hubs_delivery_is_a_bug()
    {
        var trip = NewTrip();
        var open = WithId(DeliveryGroup.Open(new NewDeliveryGroup(1, 1, Mirpur, Now, TimeZoneInfo.Utc, 2)), 40);

        Assert.Equal("trip.stop.notLocked", trip.Add(open).Error!.Code);
        Assert.Throws<InvalidOperationException>(() => trip.Add(Locked(Gulshan)));
        trip.Start(Now);
        Assert.Equal("trip.notPlanned", trip.Add(Locked(Mirpur)).Error!.Code);
    }

    [Fact]
    public void A_trip_starts_once()
    {
        var trip = NewTrip();

        var first = trip.Start(Now);
        var again = trip.Start(Now.AddMinutes(5));

        Assert.True(first.IsSuccess);
        Assert.Equal("trip.notPlanned", again.Error!.Code);
        Assert.Equal((TripStatus.Out, Now), (trip.Status, trip.StartedOn));
    }

    [Fact]
    public void Only_a_trip_still_planned_after_its_day_is_cancelled()
    {
        var past = NewTrip();
        var left = NewTrip();
        left.Start(Now);

        Assert.False(past.CancelIfPast(Today));
        Assert.True(past.CancelIfPast(Today.AddDays(1)));
        Assert.False(left.CancelIfPast(Today.AddDays(1)));
        Assert.Equal((TripStatus.Cancelled, TripStatus.Out), (past.Status, left.Status));
    }

    [Fact]
    public void A_rider_must_be_saved_before_a_trip_is_planned()
    {
        var rider = new Rider(Mirpur, "Rafiq", Phone(), new TripLoad(30, 25_000), null);

        Assert.Throws<InvalidOperationException>(() => Trip.Plan(rider, Today));
    }

    [Fact]
    public void An_order_with_every_parcel_here_goes_out_with_the_rider_and_leaves_the_hub()
    {
        var order = NewOrder(packages: 2);
        order.ReceiveAtHub(1, Mirpur, Now);
        order.ReceiveAtHub(2, Mirpur, Now);

        var handed = order.HandToRider(Mirpur);

        Assert.True(handed);
        Assert.Equal(OrderStatus.OutForDelivery, order.Status);
        Assert.All(order.Packages, package => Assert.Null(package.HubId));
        Assert.Equal("Out with the rider", order.History[^1].Note);
    }

    [Fact]
    public void An_order_not_all_here_stays_behind_unchanged()
    {
        var partly = NewOrder(packages: 2);
        partly.ReceiveAtHub(1, Mirpur, Now);
        var elsewhere = NewOrder();
        elsewhere.ReceiveAtHub(1, Gulshan, Now);
        var onShuttle = NewOrder();
        onShuttle.ReceiveAtHub(1, Gulshan, Now);
        onShuttle.LoadForShuttle(1, Gulshan, Mirpur);
        var cancelled = NewOrder();
        cancelled.ReceiveAtHub(1, Mirpur, Now);
        cancelled.MoveTo(OrderStatus.Cancelled);

        Assert.All([partly, elsewhere, onShuttle, cancelled], order => Assert.False(order.HandToRider(Mirpur)));
        Assert.Equal(
            [OrderStatus.PickedUp, OrderStatus.AtHub, OrderStatus.AtHub, OrderStatus.Cancelled],
            new[] { partly, elsewhere, onShuttle, cancelled }.Select(order => order.Status));
        Assert.Equal(Mirpur, partly.Packages[0].HubId);
    }

    private static Bike Bike(long riderId, int parcels, int grams = 25_000)
    {
        return new Bike(riderId, new TripLoad(parcels, grams), TripLoad.None);
    }

    private static WaitingDelivery Waiting(long groupId, string area, int parcels, int grams = 500)
    {
        return new WaitingDelivery(groupId, Today, area, new TripLoad(parcels, grams));
    }

    private static Trip NewTrip()
    {
        var rider = WithId(new Rider(Mirpur, "Rafiq", Phone(), new TripLoad(30, 25_000), null), 5);

        return WithId(Trip.Plan(rider, Today), 9);
    }

    private static DeliveryGroup Locked(long hubId)
    {
        return WithId(DeliveryGroup.OpenAlone(new NewDeliveryGroup(1, 1, hubId, Now, TimeZoneInfo.Utc, 2)), 30 + hubId);
    }

    private static Order NewOrder(int packages = 1)
    {
        return Order.Create(new NewOrder(
            MerchantId: 7,
            CustomerId: 1,
            AddressId: 1,
            PickupPointId: 1,
            RecipientName: "Rahim",
            CodAmount: 0,
            DeclaredValue: 0,
            Speed: DeliverySpeed.Combine,
            DoNotHold: false,
            Packages: [.. Enumerable.Range(1, packages).Select(_ => new NewPackage("Box", 500))])).Value;
    }

    private static PhoneNumber Phone()
    {
        return PhoneNumber.Parse("01722000001").Value;
    }

    /// <summary>Gives an entity the id the database would, so rules that need a saved entity can be tested.</summary>
    private static T WithId<T>(T entity, long id)
        where T : Entity
    {
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(entity, id);

        return entity;
    }
}
