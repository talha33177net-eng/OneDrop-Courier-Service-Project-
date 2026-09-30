namespace Domain.Merchants;

/// <summary>
/// The tenant's rule for shops that are often late: <see cref="AfterLateHandovers"/> orders left behind because the
/// shop had not handed them over (<c>Order.ShopLateOn</c>) within the last <see cref="WindowDays"/> days, and the
/// shop brings its parcels to the hub itself; the pickup route no longer calls. It is back on the route once fewer
/// late handovers than that are left in the window.
/// </summary>
public sealed record DropOffRule(int AfterLateHandovers, int WindowDays)
{
    /// <summary>The earliest late handover that still counts at <paramref name="now"/>.</summary>
    public DateTime WindowStart(DateTime now) => now.AddDays(-WindowDays);

    /// <summary>
    /// Until when the shop brings its parcels to the hub, or null when the route collects them: the moment the
    /// oldest late handover that keeps it over the limit leaves the window.
    /// </summary>
    public DateTime? DropsOffUntil(IEnumerable<DateTime> lateHandovers, DateTime now)
    {
        var recent = lateHandovers
            .Where(late => late > WindowStart(now))
            .OrderDescending()
            .ToList();
        if (AfterLateHandovers < 1 || recent.Count < AfterLateHandovers)
        {
            return null;
        }

        return recent[AfterLateHandovers - 1].AddDays(WindowDays);
    }
}
