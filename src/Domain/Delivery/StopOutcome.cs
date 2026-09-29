namespace Domain.Delivery;

/// <summary>What happened at the door. Stored as TINYINT. Never renumber a value that has been saved.</summary>
public enum StopOutcome : byte
{
    /// <summary>The customer paid and took at least one order.</summary>
    Delivered = 1,

    /// <summary>The customer was there and refused every order; nothing was collected.</summary>
    Refused = 2,

    /// <summary>Nobody took the parcels; they go back to the hub for the free re-attempt, or to the shop after it.</summary>
    NotHome = 3
}
