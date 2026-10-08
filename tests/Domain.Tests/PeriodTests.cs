using Domain.Common;

namespace Domain.Tests;

/// <summary>The periods a merchant reads its stats over, on the courier's own days.</summary>
public class PeriodTests
{
    private static readonly DateOnly Today = new(2026, 10, 7);

    [Theory]
    [InlineData(PeriodKind.Last7Days, "2026-10-01", "2026-10-07")]
    [InlineData(PeriodKind.Last30Days, "2026-09-08", "2026-10-07")]
    [InlineData(PeriodKind.ThisMonth, "2026-10-01", "2026-10-07")]
    [InlineData(PeriodKind.LastMonth, "2026-09-01", "2026-09-30")]
    public void A_named_period_ends_today_or_with_last_month(PeriodKind kind, string from, string to)
    {
        var period = Period.Of(kind, Today).Value;

        Assert.Equal((DateOnly.Parse(from), DateOnly.Parse(to)), (period.From, period.To));
    }

    [Fact]
    public void Last_month_in_january_is_the_december_before()
    {
        var period = Period.Of(PeriodKind.LastMonth, new DateOnly(2027, 1, 15)).Value;

        Assert.Equal((new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 31), 31), (period.From, period.To, period.Days));
    }

    [Fact]
    public void A_custom_period_needs_both_days_in_order_not_after_today_and_at_most_a_year()
    {
        Assert.Equal(3, Period.Of(PeriodKind.Custom, Today, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 3)).Value.Days);
        Assert.Equal(1, Period.Of(PeriodKind.Custom, Today, Today, Today).Value.Days);
        Assert.Equal("period.days", Period.Of(PeriodKind.Custom, Today, Today, null).Error?.Code);
        Assert.Equal("period.order", Period.Of(PeriodKind.Custom, Today, Today, Today.AddDays(-1)).Error?.Code);
        Assert.Equal("period.future", Period.Of(PeriodKind.Custom, Today, Today, Today.AddDays(1)).Error?.Code);
        Assert.Equal("period.length", Period.Of(PeriodKind.Custom, Today, Today.AddDays(-366), Today).Error?.Code);
        Assert.True(Period.Of(PeriodKind.Custom, Today, Today.AddDays(-365), Today).IsSuccess);
    }
}
