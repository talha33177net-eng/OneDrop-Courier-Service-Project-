using Domain.Grouping;

namespace Domain.Tests;

/// <summary>
/// Which delivery a new order can travel in (task 3.4a). Dhaka is UTC+6: Dhaka midnight is 18:00 UTC the day before.
/// </summary>
public class NextDayDeliveryTests
{
    private static readonly TimeZoneInfo Dhaka = TimeZoneInfo.FindSystemTimeZoneById("Asia/Dhaka");

    // Opened Mon 10:00 Dhaka
    private const string Monday = "2026-09-28 04:00";

    // Tue 00:00 Dhaka: a next-day delivery opened on Monday is delivered on Tuesday
    private const string TuesdayStarts = "2026-09-28 18:00";

    private static DateTime Utc(string value)
    {
        return DateTime.SpecifyKind(DateTime.Parse(value), DateTimeKind.Utc);
    }

    private static NewDeliveryGroup Spec(string openedOn)
    {
        return new NewDeliveryGroup(CustomerId: 1, AddressId: 1, HubId: 1, Utc(openedOn), Dhaka, JoinDays: 2);
    }

    [Fact]
    public void A_waiting_group_and_a_next_day_delivery_are_opened_as_their_kind()
    {
        Assert.Equal(DeliveryGroupKind.Waiting, DeliveryGroup.Open(Spec(Monday)).Kind);
        Assert.Equal(DeliveryGroupKind.NextDay, DeliveryGroup.OpenAlone(Spec(Monday)).Kind);
    }

    // The order is placed Monday 12:00 Dhaka; the pickup is when its shop's route next leaves
    [Theory]
    [InlineData("2026-09-28 05:30", true)] // Mon 11:30 Dhaka: collected before the delivery day
    [InlineData("2026-09-29 05:30", true)] // Tue 11:30 Dhaka: collected on the delivery day, before the riders leave
    [InlineData("2026-09-29 17:59", true)] // Tue 23:59 Dhaka: still the delivery day
    [InlineData("2026-09-29 18:00", false)] // Wed 00:00 Dhaka: too late for Tuesday's trip
    [InlineData("2026-09-30 05:30", false)] // Wed 11:30 Dhaka
    public void A_next_day_delivery_takes_an_order_whose_pickup_falls_by_its_delivery_day(string pickup, bool takes)
    {
        var fast = DeliveryGroup.OpenAlone(Spec(Monday));

        Assert.Equal(takes, fast.CanTake(Utc("2026-09-28 06:00"), Utc(pickup), waits: true, Dhaka));
        Assert.Equal(Utc(TuesdayStarts), fast.LocksAt);
    }

    [Fact]
    public void A_next_day_delivery_takes_nothing_from_a_zone_without_a_pickup_route()
    {
        var fast = DeliveryGroup.OpenAlone(Spec(Monday));

        Assert.False(fast.CanTake(Utc("2026-09-28 06:00"), pickup: null, waits: true, Dhaka));
    }

    [Fact]
    public void A_fast_or_Dont_hold_order_joins_a_next_day_delivery_it_would_not_have_to_wait_for()
    {
        var fast = DeliveryGroup.OpenAlone(Spec(Monday));

        Assert.True(fast.CanTake(Utc("2026-09-28 06:00"), Utc("2026-09-29 05:30"), waits: false, Dhaka));
    }

    [Fact]
    public void A_next_day_delivery_on_its_way_takes_nothing()
    {
        var fast = DeliveryGroup.OpenAlone(Spec(Monday));
        fast.MoveTo(DeliveryGroupStatus.Dispatched, Utc("2026-09-29 11:00"));

        Assert.False(fast.CanTake(Utc("2026-09-29 04:00"), Utc("2026-09-29 05:30"), waits: true, Dhaka));
    }

    [Theory]
    [InlineData("2026-09-28 06:00", false)] // Day 1: the open group arrives on Wednesday, not tomorrow
    [InlineData("2026-09-29 06:00", true)] // Day 2: the open group arrives tomorrow anyway
    public void A_fast_order_joins_the_open_group_only_when_it_arrives_tomorrow(string orderedOn, bool joins)
    {
        var open = DeliveryGroup.Open(Spec(Monday));

        Assert.Equal(joins, open.CanTake(Utc(orderedOn), Utc("2026-09-30 05:30"), waits: false, Dhaka));
        Assert.True(open.CanTake(Utc(orderedOn), pickup: null, waits: true, Dhaka));
    }

    [Fact]
    public void A_waiting_group_locked_at_its_deadline_takes_no_order_on_Day_3()
    {
        var open = DeliveryGroup.Open(Spec(Monday));
        open.LockIfDue(Utc("2026-09-29 18:00"));

        // Wed 08:00 Dhaka, the shop's route leaves Wed 11:30: still a new delivery (must-pass rule)
        Assert.False(open.CanTake(Utc("2026-09-30 02:00"), Utc("2026-09-30 05:30"), waits: true, Dhaka));
    }

    [Fact]
    public void Ship_now_on_Day_1_brings_the_day_forward_and_the_delivery_can_still_be_joined()
    {
        var group = DeliveryGroup.Open(Spec(Monday));
        var shippedOn = Utc("2026-09-28 06:00");
        Assert.True(group.ShipNowBringsForward(shippedOn, Dhaka));

        group.ShipNow(shippedOn, Dhaka);

        Assert.Equal(DeliveryGroupKind.ShippedNow, group.Kind);
        Assert.False(group.ShipNowBringsForward(shippedOn, Dhaka));
        Assert.True(group.CanTake(Utc("2026-09-28 20:00"), Utc("2026-09-29 05:30"), waits: true, Dhaka));
    }

    [Fact]
    public void Ship_now_on_Day_2_moves_nothing_and_the_group_stays_a_waiting_one()
    {
        var group = DeliveryGroup.Open(Spec(Monday));
        var shippedOn = Utc("2026-09-29 06:00");
        Assert.False(group.ShipNowBringsForward(shippedOn, Dhaka));

        group.ShipNow(shippedOn, Dhaka);

        Assert.Equal(DeliveryGroupKind.Waiting, group.Kind);
        Assert.Equal(Utc("2026-09-29 18:00"), group.LocksAt);
        Assert.False(group.CanTake(Utc("2026-09-29 07:00"), Utc("2026-09-30 05:30"), waits: true, Dhaka));
    }
}
