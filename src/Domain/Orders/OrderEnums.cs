namespace Domain.Orders;

/// <summary>Stored as TINYINT. Never renumber a value that has been saved.</summary>
public enum OrderStatus : byte
{
    /// <summary>Accepted from the merchant, waiting for the zone pickup route.</summary>
    Created = 1,
    PickedUp = 2,
    AtHub = 3,
    OutForDelivery = 4,
    Delivered = 5,
    Refused = 6,
    ReturnedToMerchant = 7,
    Cancelled = 8
}

/// <summary>Stored as TINYINT.</summary>
public enum DeliverySpeed : byte
{
    /// <summary>Wait and combine: joins the customer's open group and arrives on Day 3.</summary>
    Combine = 1,

    /// <summary>Next-day delivery, no waiting.</summary>
    Fast = 2
}

/// <summary>What a parcel scan did. Scanning the same parcel twice is harmless and says so; not stored.</summary>
public enum ScanOutcome
{
    Recorded,
    AlreadyRecorded
}
