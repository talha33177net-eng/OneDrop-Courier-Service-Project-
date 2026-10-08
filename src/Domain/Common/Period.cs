namespace Domain.Common;

/// <summary>The periods a merchant reads its stats over; a custom one takes its own first and last day.</summary>
public enum PeriodKind
{
    Last7Days,
    Last30Days,
    ThisMonth,
    LastMonth,
    Custom
}

/// <summary>A run of the courier's days, both ends included.</summary>
public sealed record Period(DateOnly From, DateOnly To)
{
    /// <summary>The longest custom period, a year, so one page never reads more.</summary>
    public const int MaxDays = 366;

    public int Days => To.DayNumber - From.DayNumber + 1;

    /// <summary>
    /// What <paramref name="kind"/> means on <paramref name="today"/> (the courier's date). The named periods end today,
    /// or with the last day of last month; a custom one needs both days, in order, not after today and at most a year
    /// long.
    /// </summary>
    public static Result<Period> Of(PeriodKind kind, DateOnly today, DateOnly? from = null, DateOnly? to = null)
    {
        var month = new DateOnly(today.Year, today.Month, 1);

        return kind switch
        {
            PeriodKind.Last7Days => new Period(today.AddDays(-6), today),
            PeriodKind.Last30Days => new Period(today.AddDays(-29), today),
            PeriodKind.ThisMonth => new Period(month, today),
            PeriodKind.LastMonth => new Period(month.AddMonths(-1), month.AddDays(-1)),
            PeriodKind.Custom => Custom(today, from, to),
            _ => Error.Validation("period.kind", "Choose a period.")
        };
    }

    private static Result<Period> Custom(DateOnly today, DateOnly? from, DateOnly? to)
    {
        if (from is not { } first || to is not { } last)
        {
            return Error.Validation("period.days", "Choose the first and the last day.");
        }

        if (first > last)
        {
            return Error.Validation("period.order", "Choose a first day that comes before the last.");
        }

        if (last > today)
        {
            return Error.Validation("period.future", "The last day cannot be after today.");
        }

        if (last.DayNumber - first.DayNumber + 1 > MaxDays)
        {
            return Error.Validation("period.length", "A period is at most a year long.");
        }

        return new Period(first, last);
    }
}
