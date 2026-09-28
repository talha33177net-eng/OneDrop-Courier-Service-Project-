using Domain.Network;

namespace Domain.Tests;

public class PickupRouteTests
{
    private static readonly TimeZoneInfo Dhaka = TimeZoneInfo.FindSystemTimeZoneById("Asia/Dhaka");

    private static DateTime Utc(string value)
    {
        return DateTime.SpecifyKind(DateTime.Parse(value), DateTimeKind.Utc);
    }

    // Dhaka is UTC+6: a 2 PM run leaves at 08:00 UTC
    [Theory]
    [InlineData("2026-09-28 04:00", "2026-09-28 08:00")] // Mon 10:00 Dhaka -> today's run
    [InlineData("2026-09-28 08:00", "2026-09-28 08:00")] // the moment it leaves still counts as today's
    [InlineData("2026-09-28 08:00:01", "2026-09-29 08:00")] // it has left -> tomorrow's
    [InlineData("2026-09-28 18:30", "2026-09-29 08:00")] // Tue 00:30 Dhaka is still Monday in UTC
    public void The_next_run_is_todays_until_it_has_left_then_tomorrows(string now, string next)
    {
        var route = new PickupRoute(zoneId: 1, new TimeOnly(14, 0));

        var pickup = route.NextPickup(Utc(now), Dhaka);

        Assert.Equal(Utc(next), pickup);
        Assert.Equal(DateTimeKind.Utc, pickup.Kind);
    }

    [Fact]
    public void Tomorrows_run_uses_tomorrows_clock_when_summer_time_ends_overnight()
    {
        var london = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
        var route = new PickupRoute(zoneId: 1, new TimeOnly(14, 0));

        // Sat 24 Oct 15:00 BST (UTC+1), after the run; clocks go back on Sunday, so 2 PM is 14:00 UTC
        Assert.Equal(Utc("2026-10-25 14:00"), route.NextPickup(Utc("2026-10-24 14:00"), london));
    }
}
