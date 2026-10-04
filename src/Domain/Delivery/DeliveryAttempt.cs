using Domain.Common;
using Domain.Parcels;

namespace Domain.Delivery;

/// <summary>What happened at the door. Stored as TINYINT. Never renumber a value that has been saved.</summary>
public enum AttemptOutcome : byte
{
    Delivered = 1,
    PartlyDelivered = 2,

    /// <summary>The recipient asked for another day or could not be reached; the parcel goes back to the hub.</summary>
    Hold = 3,

    /// <summary>The recipient refused the parcel; it returns to the merchant.</summary>
    Refused = 4
}

/// <summary>
/// One parcel on a rider's run: handed over at the hub, then delivered, held or refused at the door. A parcel held for
/// another day gets a new attempt on another run. Carries <see cref="MerchantId"/> so a merchant can read its own
/// parcels' attempts without a join.
/// </summary>
public class DeliveryAttempt : TenantEntity, IMerchantOwned
{
    private DeliveryAttempt()
    {
    }

    internal DeliveryAttempt(DeliveryRun run, Parcel parcel, DateTime now)
    {
        RunId = run.Id;
        RiderId = run.RiderId;
        ParcelId = parcel.Id;
        MerchantId = parcel.MerchantId;
        AssignedOn = now;
    }

    public long RunId { get; private set; }

    public long RiderId { get; private set; }

    public long ParcelId { get; private set; }

    public long MerchantId { get; private set; }

    public DateTime AssignedOn { get; private set; }

    /// <summary>Null while the rider still has the parcel.</summary>
    public AttemptOutcome? Outcome { get; private set; }

    /// <summary>Cash collected at the door: set for a delivery, 0 otherwise.</summary>
    public decimal CollectedAmount { get; private set; }

    public string? Reason { get; private set; }

    public DateTime? CompletedOn { get; private set; }

    public bool IsDone => Outcome is not null;

    /// <summary>Records what happened, once. The parcel itself has already been moved by the same use case.</summary>
    public Result Complete(AttemptOutcome outcome, decimal collected, string? reason, DateTime now)
    {
        if (IsDone)
        {
            return Error.Conflict("attempt.done", "What happened to this parcel has already been recorded.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(collected);
        if (outcome is not (AttemptOutcome.Delivered or AttemptOutcome.PartlyDelivered) && collected != 0)
        {
            throw new InvalidOperationException($"No cash is collected for a parcel that ends {outcome}.");
        }

        Outcome = outcome;
        CollectedAmount = collected;
        Reason = reason.NullIfBlank();
        CompletedOn = now;

        return Result.Success();
    }
}
