using Domain.Delivery;

namespace Domain.Tests;

/// <summary>Riders, their daily runs and delivery attempts, and pickup requests.</summary>
public class DeliveryTests
{
    private static readonly DateTime Now = new(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 10, 4);

    private static Rider NewRider(long hubId = Build.Mirpur, long id = 5)
    {
        return Build.WithId(Rider.Create(hubId, "Rafiq Hasan", "01722000001", userId: 30).Value, id);
    }

    [Fact]
    public void A_rider_needs_a_name_and_a_mobile_number()
    {
        Assert.Equal("rider.name", Rider.Create(1, " ", "01722000001", null).Error?.Code);
        Assert.Equal("phone.invalid", Rider.Create(1, "Rafiq", "12345", null).Error?.Code);
        Assert.Equal("+8801722000001", NewRider().Phone);
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
        Assert.Equal("pickup.notAssigned", pickup.Complete(3, Now).Error?.Code);
        Assert.Equal("pickup.rider", pickup.Assign(NewRider(hubId: Build.Gulshan)).Error?.Code);
        Assert.True(pickup.Assign(NewRider()).IsSuccess);
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

        Assert.Equal("pickup.rider", pickup.Assign(rider).Error?.Code);
        Assert.True(pickup.Cancel().IsSuccess);
    }
}
