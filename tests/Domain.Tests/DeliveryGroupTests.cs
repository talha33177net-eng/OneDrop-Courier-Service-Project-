using Domain.Common;
using Domain.Grouping;

namespace Domain.Tests;

public class DeliveryGroupTests
{
    private static readonly TimeZoneInfo Dhaka = TimeZoneInfo.FindSystemTimeZoneById("Asia/Dhaka");

    private static DateTime Utc(string value)
    {
        return DateTime.SpecifyKind(DateTime.Parse(value), DateTimeKind.Utc);
    }

    private static DeliveryGroup OpenAt(string openedOnUtc, int joinDays = 2)
    {
        return DeliveryGroup.Open(new NewDeliveryGroup(
            CustomerId: 1,
            AddressId: 1,
            HubId: 1,
            OpenedOn: Utc(openedOnUtc),
            TimeZone: Dhaka,
            JoinDays: joinDays));
    }

    // Dhaka is UTC+6, so Dhaka midnight is 18:00 UTC the day before
    [Theory]
    [InlineData("2026-09-28 04:00", "2026-09-29 18:00")] // Mon 10:00 Dhaka -> locks at Wed 00:00 Dhaka
    [InlineData("2026-09-28 17:59", "2026-09-29 18:00")] // Mon 23:59 Dhaka -> still Monday
    [InlineData("2026-09-27 18:00", "2026-09-29 18:00")] // Mon 00:00 Dhaka is Sunday in UTC
    [InlineData("2026-09-28 18:00", "2026-09-30 18:00")] // Tue 00:00 Dhaka is the next Day 1
    public void Lock_time_is_the_start_of_Day_3_in_the_tenants_time_zone(string openedOn, string locksAt)
    {
        var group = OpenAt(openedOn);

        Assert.Equal(DeliveryGroupStatus.Open, group.Status);
        Assert.Equal(Utc(locksAt), group.LocksAt);
        Assert.Equal(DateTimeKind.Utc, group.LocksAt.Kind);
        Assert.Null(group.LockedOn);
    }

    [Fact]
    public void Join_days_come_from_the_tenant()
    {
        Assert.Equal(Utc("2026-09-28 18:00"), OpenAt("2026-09-28 04:00", joinDays: 1).LocksAt);
    }

    [Theory]
    [InlineData("2026-09-28 04:00", true)] // Day 1, the moment it opened
    [InlineData("2026-09-29 17:59:59", true)] // Day 2, 23:59:59 Dhaka
    [InlineData("2026-09-29 18:00", false)] // Day 3, 00:00 Dhaka: starts a new group
    [InlineData("2026-09-30 12:00", false)]
    public void Orders_join_on_Day_1_and_Day_2_only(string orderedOn, bool joins)
    {
        Assert.Equal(joins, OpenAt("2026-09-28 04:00").CanJoin(Utc(orderedOn)));
    }

    [Fact]
    public void The_deadline_never_moves()
    {
        var group = OpenAt("2026-09-28 04:00");

        Assert.True(group.CanJoin(Utc("2026-09-29 12:00")));
        Assert.Equal(Utc("2026-09-29 18:00"), group.LocksAt);
    }

    [Fact]
    public void A_group_locked_early_by_Ship_now_takes_no_more_orders()
    {
        var group = OpenAt("2026-09-28 04:00");

        Assert.True(group.MoveTo(DeliveryGroupStatus.Locked, Utc("2026-09-28 06:00")).IsSuccess);

        Assert.Equal(Utc("2026-09-28 06:00"), group.LockedOn);
        Assert.False(group.CanJoin(Utc("2026-09-28 07:00")));
    }

    [Fact]
    public void Status_follows_the_delivery_path()
    {
        var group = OpenAt("2026-09-28 04:00");

        Assert.True(group.MoveTo(DeliveryGroupStatus.Locked, Utc("2026-09-29 18:00")).IsSuccess);
        Assert.True(group.MoveTo(DeliveryGroupStatus.Dispatched, Utc("2026-09-30 11:00")).IsSuccess);
        Assert.True(group.MoveTo(DeliveryGroupStatus.Delivered, Utc("2026-09-30 13:00")).IsSuccess);

        Assert.All(Enum.GetValues<DeliveryGroupStatus>(), status => Assert.False(group.CanMoveTo(status)));
    }

    [Fact]
    public void A_re_attempt_goes_back_to_Locked_and_keeps_the_first_lock_time()
    {
        var group = OpenAt("2026-09-28 04:00");
        group.MoveTo(DeliveryGroupStatus.Locked, Utc("2026-09-29 18:00"));
        group.MoveTo(DeliveryGroupStatus.Dispatched, Utc("2026-09-30 11:00"));

        Assert.True(group.MoveTo(DeliveryGroupStatus.Locked, Utc("2026-09-30 15:00")).IsSuccess);

        Assert.Equal(Utc("2026-09-29 18:00"), group.LockedOn);
    }

    [Theory]
    [InlineData(DeliveryGroupStatus.Dispatched)]
    [InlineData(DeliveryGroupStatus.Delivered)]
    [InlineData(DeliveryGroupStatus.Open)]
    public void An_open_group_cannot_skip_ahead(DeliveryGroupStatus target)
    {
        var group = OpenAt("2026-09-28 04:00");

        var moved = group.MoveTo(target, Utc("2026-09-28 05:00"));

        Assert.Equal(ErrorType.Conflict, moved.Error!.Type);
        Assert.Equal(DeliveryGroupStatus.Open, group.Status);
    }

    [Fact]
    public void A_cancelled_group_is_final()
    {
        var group = OpenAt("2026-09-28 04:00");
        group.MoveTo(DeliveryGroupStatus.Cancelled, Utc("2026-09-28 05:00"));

        Assert.All(Enum.GetValues<DeliveryGroupStatus>(), status => Assert.False(group.CanMoveTo(status)));
    }

    [Theory]
    [InlineData("2026-09-28 04:00", "2026-09-28 18:00")] // Mon 10:00 Dhaka -> Tuesday
    [InlineData("2026-09-28 18:30", "2026-09-29 18:00")] // Tue 00:30 Dhaka -> Wednesday
    public void An_order_that_does_not_wait_travels_alone_the_next_day(string openedOn, string locksAt)
    {
        var group = DeliveryGroup.OpenAlone(new NewDeliveryGroup(1, 1, 1, Utc(openedOn), Dhaka, JoinDays: 2));

        Assert.Equal(DeliveryGroupStatus.Locked, group.Status);
        Assert.Equal(Utc(openedOn), group.LockedOn);
        Assert.Equal(Utc(locksAt), group.LocksAt);
        Assert.False(group.CanJoin(Utc(openedOn)));
    }

    [Fact]
    public void A_group_is_not_locked_before_its_deadline()
    {
        var group = OpenAt("2026-09-28 04:00");

        Assert.False(group.LockIfDue(Utc("2026-09-29 17:59:59")));
        Assert.Equal(DeliveryGroupStatus.Open, group.Status);
        Assert.Null(group.LockedOn);
    }

    [Fact]
    public void A_group_locked_late_records_its_deadline_as_the_lock_time()
    {
        var group = OpenAt("2026-09-28 04:00");

        Assert.True(group.LockIfDue(Utc("2026-09-30 09:00")));
        Assert.Equal(DeliveryGroupStatus.Locked, group.Status);
        Assert.Equal(Utc("2026-09-29 18:00"), group.LockedOn);
    }

    [Fact]
    public void Only_an_open_group_is_locked_at_its_deadline()
    {
        var group = OpenAt("2026-09-28 04:00");
        group.MoveTo(DeliveryGroupStatus.Locked, Utc("2026-09-28 06:00"));

        Assert.False(group.LockIfDue(Utc("2026-09-30 09:00")));
        Assert.Equal(Utc("2026-09-28 06:00"), group.LockedOn);
    }

    [Fact]
    public void The_opening_time_must_be_UTC()
    {
        var spec = new NewDeliveryGroup(1, 1, 1, new DateTime(2026, 9, 28, 10, 0, 0, DateTimeKind.Local), Dhaka, 2);

        Assert.Throws<ArgumentException>(() => DeliveryGroup.Open(spec));
    }
}
