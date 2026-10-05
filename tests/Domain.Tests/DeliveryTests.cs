using Domain.Delivery;

namespace Domain.Tests;

/// <summary>Riders, their daily runs and delivery attempts, and pickup requests.</summary>
public class DeliveryTests
{
    private static readonly DateTime Now = new(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 10, 4);

    private static Rider NewRider(long hubId = Build.Mirpur, long id = 5, Vehicle vehicle = Vehicle.Motorbike)
    {
        return Build.WithId(Rider.Create(hubId, "Rafiq Hasan", "01722000001", vehicle, userId: 30).Value, id);
    }

    [Fact]
    public void A_rider_needs_a_name_a_mobile_number_and_a_vehicle()
    {
        Assert.Equal("rider.name", Rider.Create(1, " ", "01722000001", Vehicle.Motorbike, null).Error?.Code);
        Assert.Equal("phone.invalid", Rider.Create(1, "Rafiq", "12345", Vehicle.Motorbike, null).Error?.Code);
        Assert.Equal("rider.vehicle", Rider.Create(1, "Rafiq", "01722000001", default, null).Error?.Code);
        Assert.Equal("+8801722000001", NewRider().Phone);
        Assert.Equal(Vehicle.Bicycle, NewRider(vehicle: Vehicle.Bicycle).Vehicle);
    }

    [Fact]
    public void A_run_takes_parcels_while_open_and_records_each_outcome_once()
    {
        var run = Build.WithId(DeliveryRun.Open(NewRider(), Today), 9);
        var parcel = Build.OutForDelivery();
        var attempt = run.Add(parcel, Now).Value;

        Assert.Equal(run.Id, attempt.RunId);
        Assert.Equal(5, attempt.RiderId);
        Assert.Equal(7, attempt.MerchantId);
        Assert.False(attempt.IsDone);

        Assert.True(attempt.Complete(AttemptOutcome.Delivered, 1250, null, Now).IsSuccess);
        Assert.Equal("attempt.done", attempt.Complete(AttemptOutcome.Hold, 0, "x", Now).Error?.Code);
        Assert.Throws<InvalidOperationException>(() => run.Add(parcel, Now).Value.Complete(AttemptOutcome.Hold, 100, "x", Now));
    }

    [Fact]
    public void Closing_a_run_records_the_cash_and_any_shortfall_once()
    {
        var run = Build.WithId(DeliveryRun.Open(NewRider(), Today), 9);

        Assert.Equal("run.cash", run.Close(2085, -1, Now).Error?.Code);
        Assert.True(run.Close(2085, 2000, Now).IsSuccess);
        Assert.Equal(85, run.CashShort);
        Assert.Equal(RunStatus.Closed, run.Status);
        Assert.Equal("run.closed", run.Close(2085, 2085, Now).Error?.Code);
        Assert.Equal("run.closed", run.Add(Build.OutForDelivery(), Now).Error?.Code);
    }

    [Fact]
    public void A_pickup_is_asked_for_up_to_a_week_ahead_and_goes_requested_assigned_completed()
    {
        Assert.Equal("pickup.date", PickupRequest.Create(7, 1, Build.Mirpur, Today.AddDays(-1), Today, 3, null).Error?.Code);
        Assert.Equal("pickup.date", PickupRequest.Create(7, 1, Build.Mirpur, Today.AddDays(8), Today, 3, null).Error?.Code);
        Assert.Equal("pickup.parcels", PickupRequest.Create(7, 1, Build.Mirpur, Today, Today, 0, null).Error?.Code);

        var pickup = PickupRequest.Create(7, 1, Build.Mirpur, Today, Today, 3, "At the counter").Value;
        var load = PickupRequest.ToCollect(3, []);
        Assert.Equal("pickup.notAssigned", pickup.Complete(3, Now).Error?.Code);
        Assert.Equal("pickup.rider", pickup.Assign(NewRider(hubId: Build.Gulshan), Build.Fleet(), load).Error?.Code);
        Assert.True(pickup.Assign(NewRider(), Build.Fleet(), load).IsSuccess);
        Assert.True(pickup.Complete(2, Now).IsSuccess);

        Assert.Equal(PickupStatus.Completed, pickup.Status);
        Assert.Equal(2, pickup.PickedParcels);
        Assert.Equal("pickup.closed", pickup.Cancel().Error?.Code);
    }

    [Fact]
    public void A_stopped_rider_is_not_sent_on_a_pickup()
    {
        var rider = NewRider();
        rider.Deactivate();
        var pickup = PickupRequest.Create(7, 1, Build.Mirpur, Today, Today, 1, null).Value;

        Assert.Equal("pickup.rider", pickup.Assign(rider, Build.Fleet(), PickupRequest.ToCollect(1, [])).Error?.Code);
        Assert.True(pickup.Cancel().IsSuccess);
    }

    [Theory]
    [InlineData(0, 30_000, 10_000, "capacity.parcels")]
    [InlineData(40, 500, 100, "capacity.load")]
    [InlineData(40, 30_000, 50, "capacity.parcel")]
    [InlineData(40, 30_000, 31_000, "capacity.parcel")]
    public void A_vehicle_carries_at_least_one_parcel_and_a_parcel_no_heavier_than_the_load(int parcels, int load, int heaviest, string code)
    {
        Assert.Equal(code, VehicleCapacity.Create(Vehicle.Motorbike, new CapacityValues(parcels, load, heaviest)).Error?.Code);
    }

    [Fact]
    public void A_rider_takes_parcels_until_their_vehicle_is_full_in_number_or_weight()
    {
        var motorbike = Build.Fleet()[Vehicle.Motorbike]!;

        Assert.True(motorbike.Take(new Load(39, 20_000, 2_000), "OD1", 1_000).IsSuccess);
        Assert.Equal("capacity.parcels", motorbike.Take(new Load(40, 20_000, 2_000), "OD1", 1_000).Error?.Code);
        Assert.Equal("capacity.load", motorbike.Take(new Load(10, 29_500, 2_000), "OD1", 1_000).Error?.Code);
        var heavy = motorbike.Take(new Load(0, 0, 0), "OD1", 12_000).Error!;
        Assert.Equal("capacity.parcel", heavy.Code);
        Assert.Equal("OD1 weighs 12 kg; a motorbike takes parcels up to 10 kg.", heavy.Message);
    }

    [Fact]
    public void A_load_adds_up_its_parcels_and_keeps_the_heaviest()
    {
        var load = Load.Of([500, 2_000, 1_200]).With(300);

        Assert.Equal(new Load(4, 4_000, 2_000), load);
        Assert.Equal(new Load(0, 0, 0), Load.Of([]));
    }

    [Fact]
    public void A_vehicle_with_no_capacity_set_takes_anything()
    {
        var fleet = new Fleet([]);

        Assert.True(fleet.Take(Vehicle.Bicycle, new Load(500, 900_000, 30_000), "OD1", 30_000).IsSuccess);
        Assert.True(fleet.Collect(Vehicle.Bicycle, new Load(900, 0, 0)).IsSuccess);
    }

    [Fact]
    public void A_pickup_is_sized_by_what_is_booked_or_what_the_merchant_expects_whichever_is_more()
    {
        Assert.Equal(new Load(10, 1_500, 1_000), PickupRequest.ToCollect(10, [500, 1_000]));
        Assert.Equal(new Load(3, 1_800, 800), PickupRequest.ToCollect(2, [500, 500, 800]));
    }

    [Fact]
    public void The_hub_sends_the_smallest_vehicle_that_carries_a_pickup_in_one_trip()
    {
        var fleet = Build.Fleet();

        Assert.Equal(Vehicle.Bicycle, fleet.For(new Load(10, 4_000, 1_000)));
        Assert.Equal(Vehicle.Motorbike, fleet.For(new Load(30, 4_000, 1_000)));
        Assert.Equal(Vehicle.Motorbike, fleet.For(new Load(10, 4_000, 8_000)));
        Assert.Equal(Vehicle.Van, fleet.For(new Load(120, 60_000, 1_000)));
        Assert.Equal(Vehicle.Van, fleet.For(new Load(2_000, 0, 0)));
    }

    [Fact]
    public void A_pickup_too_big_for_the_riders_vehicle_is_refused_and_says_what_to_send()
    {
        var pickup = PickupRequest.Create(7, 1, Build.Mirpur, Today, Today, 120, null).Value;
        var load = PickupRequest.ToCollect(pickup.ExpectedParcels, [1_000, 2_000]);

        var refused = pickup.Assign(NewRider(), Build.Fleet(), load).Error!;
        Assert.Equal("pickup.vehicle", refused.Code);
        Assert.Equal("This pickup is 120 parcels; a motorbike carries 40. Send a rider with a pickup van.", refused.Message);
        Assert.Null(pickup.RiderId);
        Assert.True(pickup.Assign(NewRider(id: 6, vehicle: Vehicle.Van), Build.Fleet(), load).IsSuccess);
    }

    [Fact]
    public void A_pickup_nothing_carries_in_one_trip_goes_to_the_biggest_vehicle()
    {
        var pickup = PickupRequest.Create(7, 1, Build.Mirpur, Today, Today, 1_000, null).Value;
        var load = PickupRequest.ToCollect(pickup.ExpectedParcels, []);

        Assert.Equal("pickup.vehicle", pickup.Assign(NewRider(), Build.Fleet(), load).Error?.Code);
        Assert.True(pickup.Assign(NewRider(vehicle: Vehicle.Van), Build.Fleet(), load).IsSuccess);
    }
}
