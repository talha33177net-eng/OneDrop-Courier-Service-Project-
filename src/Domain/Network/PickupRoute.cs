using Domain.Common;

namespace Domain.Network;

/// <summary>
/// The daily pickup run of one zone (e.g. Mirpur at 2 PM): a collector visits every merchant pickup point in the zone
/// that has parcels waiting and brings them to the hub. One active route per zone. The time is the tenant's own
/// setting, in its time zone.
/// </summary>
public class PickupRoute : TenantEntity, IArchivable
{
    private PickupRoute()
    {
    }

    public PickupRoute(long zoneId, TimeOnly pickupTime)
    {
        ZoneId = zoneId;
        PickupTime = pickupTime;
    }

    public long ZoneId { get; private set; }

    public Zone? Zone { get; private set; }

    /// <summary>When the collector leaves each day, in the tenant's time zone.</summary>
    public TimeOnly PickupTime { get; private set; }

    public bool Archived { get; private set; }

    /// <summary>
    /// The next departure at or after <paramref name="utcNow"/>, in UTC: today's run until it has left, then
    /// tomorrow's. Days are the tenant's days.
    /// </summary>
    public DateTime NextPickup(DateTime utcNow, TimeZoneInfo timeZone)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utcNow, timeZone));
        var todayRun = TimeZoneInfo.ConvertTimeToUtc(today.ToDateTime(PickupTime), timeZone);

        return todayRun >= utcNow
            ? todayRun
            : TimeZoneInfo.ConvertTimeToUtc(today.AddDays(1).ToDateTime(PickupTime), timeZone);
    }
}
