namespace Domain.Grouping;

/// <summary>
/// Packages per delivery: the number the business lives on (target 2 or more). Counted over deliveries handed over
/// and the packages the customer took. Weeks are the seven days ending on a day, so no week-start convention is needed.
/// </summary>
public readonly record struct DeliveryDensity(int Deliveries, int Packages)
{
    public static DeliveryDensity None => new(0, 0);

    /// <summary>Null with no delivery; otherwise rounded to one decimal.</summary>
    public decimal? PackagesPerDelivery => Deliveries == 0
        ? null
        : Math.Round((decimal)Packages / Deliveries, 1, MidpointRounding.AwayFromZero);

    public static DeliveryDensity operator +(DeliveryDensity left, DeliveryDensity right)
    {
        return new DeliveryDensity(left.Deliveries + right.Deliveries, left.Packages + right.Packages);
    }

    /// <summary>
    /// How many whole weeks before the week ending <paramref name="today"/> the <paramref name="day"/> falls: 0 for
    /// today and the six days before it, 1 for the seven days before those, and so on.
    /// </summary>
    public static int WeeksBack(DateOnly today, DateOnly day)
    {
        return (today.DayNumber - day.DayNumber) / 7;
    }
}
