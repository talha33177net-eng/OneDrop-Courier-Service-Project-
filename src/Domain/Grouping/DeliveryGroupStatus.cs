namespace Domain.Grouping;

/// <summary>Stored as TINYINT. Never renumber a value that has been saved.</summary>
public enum DeliveryGroupStatus : byte
{
    /// <summary>Collecting orders until the lock time. At most one per customer and address.</summary>
    Open = 1,

    /// <summary>No more orders join; waiting at the hub for delivery day.</summary>
    Locked = 2,

    /// <summary>With a rider on delivery day.</summary>
    Dispatched = 3,

    Delivered = 4,

    /// <summary>Nothing was delivered: every order was cancelled, refused or returned.</summary>
    Cancelled = 5
}
