namespace Domain.Grouping;

/// <summary>How a delivery reaches its delivery day. Stored as TINYINT. Never renumber a value that has been saved.</summary>
public enum DeliveryGroupKind : byte
{
    /// <summary>Opened to wait for other shops; delivered on the day after the last day to join (Day 3).</summary>
    Waiting = 1,

    /// <summary>
    /// Opened by a Deliver fast or Don't hold order: delivered the next day. Other orders join it while their pickup
    /// route still runs by its delivery day.
    /// </summary>
    NextDay = 2,

    /// <summary>
    /// A waiting delivery the customer brought forward with Ship now, paying the fast difference. Joined like a
    /// next-day delivery.
    /// </summary>
    ShippedNow = 3
}
