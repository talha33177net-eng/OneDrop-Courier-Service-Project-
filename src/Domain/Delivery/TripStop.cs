using Domain.Common;
using Domain.Grouping;
using Domain.Payments;

namespace Domain.Delivery;

/// <summary>
/// A delivery on a trip. <see cref="DeliveryDate"/> is copied from the trip so the database can keep a delivery on
/// one trip a day (<c>UX_TripStop_DeliveryGroup_DeliveryDate</c>): two planners running at once cannot both take it.
/// A delivery back at the hub after a failed attempt gets a new stop on another day's trip.
/// </summary>
public class TripStop : TenantEntity
{
    private TripStop()
    {
    }

    internal TripStop(Trip trip, DeliveryGroup group)
    {
        TripId = trip.Id;
        DeliveryGroupId = group.Id;
        DeliveryDate = trip.DeliveryDate;
    }

    public long TripId { get; private set; }

    public long DeliveryGroupId { get; private set; }

    public DateOnly DeliveryDate { get; private set; }

    /// <summary>What happened at the door; null until the rider has been there.</summary>
    public StopOutcome? Outcome { get; private set; }

    /// <summary>
    /// This delivery's part of the delivery fee the rider collected at the visit. A visit covering several of the
    /// customer's deliveries collects one fee, split over their stops.
    /// </summary>
    public decimal? FeeCollected { get; private set; }

    /// <summary>The shops' cash on delivery collected for this delivery's accepted orders.</summary>
    public decimal? CodCollected { get; private set; }

    public DateTime? CompletedOn { get; private set; }

    /// <summary>What the customer paid the visit with; every stop of the visit points at the same payment.</summary>
    public long? PaymentId { get; private set; }

    /// <summary>Set with <see cref="Complete"/> so a payment made at the door is saved with the stop.</summary>
    public Payment? Payment { get; private set; }

    /// <summary>A stop already done at the door.</summary>
    public static Error AlreadyDone => Error.Conflict("trip.stop.done", "This stop is already done.");

    /// <summary>
    /// Records what happened at the door, what the rider collected and the payment it came with. A stop is done once.
    /// "No fee, no handover": money is only ever collected with a paid payment.
    /// </summary>
    public Result Complete(StopOutcome outcome, decimal fee, decimal cod, Payment? payment, DateTime now)
    {
        if (Outcome is not null)
        {
            return AlreadyDone;
        }

        ArgumentOutOfRangeException.ThrowIfNegative(fee);
        ArgumentOutOfRangeException.ThrowIfNegative(cod);
        if (outcome != StopOutcome.Delivered && fee + cod != 0)
        {
            throw new InvalidOperationException($"Nothing is collected at a stop that ends {outcome}.");
        }

        if (payment is not null && (outcome != StopOutcome.Delivered || payment.Status != PaymentStatus.Paid))
        {
            throw new InvalidOperationException("Only a delivered stop comes with a payment, and only a paid one.");
        }

        if (payment is null && fee + cod != 0)
        {
            throw new InvalidOperationException("Money collected at the door needs its payment.");
        }

        Outcome = outcome;
        FeeCollected = fee;
        CodCollected = cod;
        CompletedOn = now;
        Payment = payment;

        return Result.Success();
    }
}
