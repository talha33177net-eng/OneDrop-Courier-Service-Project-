namespace Domain.Delivery;

/// <summary>Stored as TINYINT. Never renumber a value that has been saved.</summary>
public enum TripStatus : byte
{
    /// <summary>Deliveries are being put on it; the rider has not left yet.</summary>
    Planned = 1,

    /// <summary>The rider has taken the parcels out.</summary>
    Out = 2,

    /// <summary>The rider is back and every stop has an outcome.</summary>
    Finished = 3,

    /// <summary>Planned for a day that passed without the rider leaving; its deliveries are planned again.</summary>
    Cancelled = 4
}
