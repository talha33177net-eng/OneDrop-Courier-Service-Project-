namespace Domain.Parcels;

/// <summary>Stored as TINYINT. Never renumber a value that has been saved.</summary>
public enum ParcelStatus : byte
{
    /// <summary>Booked by the merchant, waiting to be picked up.</summary>
    Pending = 1,

    /// <summary>Collected from the merchant by a rider, on the way to the hub.</summary>
    PickedUp = 2,

    /// <summary>At a hub (<see cref="Parcel.CurrentHubId"/>), being sorted or waiting for a rider.</summary>
    AtHub = 3,

    /// <summary>Travelling between hubs, to <see cref="Parcel.TransferToHubId"/>.</summary>
    InTransit = 4,

    /// <summary>With a rider (<see cref="Parcel.RiderId"/>) on the way to the recipient.</summary>
    OutForDelivery = 5,

    /// <summary>A delivery attempt failed and the recipient asked for another day; delivered again from the hub.</summary>
    OnHold = 6,

    /// <summary>Handed over and the whole cash on delivery collected. Final.</summary>
    Delivered = 7,

    /// <summary>The recipient kept part of the parcel and paid less than the cash on delivery. Final.</summary>
    PartlyDelivered = 8,

    /// <summary>On the way back to the merchant: refused at the door, or the merchant or hub asked for it back.</summary>
    Returning = 9,

    /// <summary>Handed back to the merchant. Final.</summary>
    Returned = 10,

    /// <summary>Cancelled by the merchant before it was picked up. Final.</summary>
    Cancelled = 11
}

/// <summary>What a scan did. Scanning the same parcel twice is harmless and says so; not stored.</summary>
public enum ScanOutcome
{
    Recorded,
    AlreadyRecorded
}

public static class ParcelStatuses
{
    /// <summary>Statuses a parcel never leaves.</summary>
    public static readonly ParcelStatus[] Final =
        [ParcelStatus.Delivered, ParcelStatus.PartlyDelivered, ParcelStatus.Returned, ParcelStatus.Cancelled];

    /// <summary>Picked up and not yet finished: the parcel is somewhere in the courier's hands.</summary>
    public static readonly ParcelStatus[] InProgress =
    [
        ParcelStatus.PickedUp, ParcelStatus.AtHub, ParcelStatus.InTransit, ParcelStatus.OutForDelivery,
        ParcelStatus.OnHold, ParcelStatus.Returning
    ];

    /// <summary>Picked up and still on its way to the recipient: the statuses a parcel can be late in.</summary>
    public static readonly ParcelStatus[] ToDeliver =
    [
        ParcelStatus.PickedUp, ParcelStatus.AtHub, ParcelStatus.InTransit, ParcelStatus.OutForDelivery,
        ParcelStatus.OnHold
    ];

    public static bool IsFinal(this ParcelStatus status)
    {
        return Final.Contains(status);
    }

    /// <summary>How the status is written for people: "Out for delivery".</summary>
    public static string DisplayName(this ParcelStatus status)
    {
        return status switch
        {
            ParcelStatus.Pending => "Pending pickup",
            ParcelStatus.PickedUp => "Picked up",
            ParcelStatus.AtHub => "At hub",
            ParcelStatus.InTransit => "In transit",
            ParcelStatus.OutForDelivery => "Out for delivery",
            ParcelStatus.OnHold => "On hold",
            ParcelStatus.Delivered => "Delivered",
            ParcelStatus.PartlyDelivered => "Partly delivered",
            ParcelStatus.Returning => "Returning",
            ParcelStatus.Returned => "Returned",
            _ => "Cancelled"
        };
    }
}
