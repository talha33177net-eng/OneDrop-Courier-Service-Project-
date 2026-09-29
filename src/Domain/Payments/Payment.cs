using Domain.Common;

namespace Domain.Payments;

/// <summary>Everything a payment at the door needs. <see cref="DeliveryGroupId"/> is the visit's first delivery.</summary>
public sealed record DoorPayment(
    long CustomerId,
    long TripId,
    long RiderId,
    long DeliveryGroupId,
    PaymentMethod Method,
    decimal Fee,
    decimal Cod);

/// <summary>A payment was received: text the customer a receipt.</summary>
public sealed record PaymentReceived(Payment Payment) : IDomainEvent;

/// <summary>
/// Money the customer paid OneDrop: at the door, the visit's delivery fee and the shops' cash on delivery together,
/// kept apart (<see cref="Fee"/>, <see cref="Cod"/>) so the ledger can pay each shop its part. Cash is paid when the
/// rider records it. A QR payment starts <see cref="PaymentStatus.Pending"/> with the gateway's reference and the
/// link the QR holds, and is paid once the gateway says so.
/// </summary>
public class Payment : TenantEntity
{
    private Payment()
    {
    }

    public long CustomerId { get; private set; }

    /// <summary>The trip the rider collected it on; set for a payment at the door.</summary>
    public long? TripId { get; private set; }

    /// <summary>The rider who collected it: cash is theirs to hand in at the hub.</summary>
    public long? RiderId { get; private set; }

    /// <summary>The delivery the payment is for; at the door, the first delivery of the visit.</summary>
    public long DeliveryGroupId { get; private set; }

    public PaymentPurpose Purpose { get; private set; }

    public PaymentMethod Method { get; private set; }

    public PaymentStatus Status { get; private set; }

    public decimal Fee { get; private set; }

    public decimal Cod { get; private set; }

    public decimal Amount => Fee + Cod;

    /// <summary>The gateway's id for a QR payment, to ask whether it is paid.</summary>
    public string? GatewayReference { get; private set; }

    /// <summary>What the QR holds: the gateway's payment link for this amount.</summary>
    public string? PaymentLink { get; private set; }

    public DateTime? PaidOn { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    /// <summary>
    /// Cash, paid as the rider records it; bKash or Nagad, pending until the gateway has the money
    /// (<see cref="RequestedAs"/>, then <see cref="MarkPaid"/>).
    /// </summary>
    public static Payment AtTheDoor(DoorPayment spec, DateTime now)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(spec.Fee);
        ArgumentOutOfRangeException.ThrowIfNegative(spec.Cod);
        ArgumentOutOfRangeException.ThrowIfZero(spec.Fee + spec.Cod);

        var payment = new Payment
        {
            CustomerId = spec.CustomerId,
            TripId = spec.TripId,
            RiderId = spec.RiderId,
            DeliveryGroupId = spec.DeliveryGroupId,
            Purpose = PaymentPurpose.Door,
            Method = spec.Method,
            Fee = spec.Fee,
            Cod = spec.Cod,
            Status = PaymentStatus.Pending
        };
        if (spec.Method == PaymentMethod.Cash)
        {
            payment.MarkPaid(now);
        }

        return payment;
    }

    /// <summary>The gateway's reference and the link the customer pays through, for a QR payment.</summary>
    public void RequestedAs(string gatewayReference, string paymentLink)
    {
        if (Method == PaymentMethod.Cash || Status != PaymentStatus.Pending || GatewayReference is not null)
        {
            throw new InvalidOperationException("Only a new QR payment is requested from the gateway.");
        }

        GatewayReference = gatewayReference;
        PaymentLink = paymentLink;
    }

    /// <summary>The money has arrived. Paying again changes nothing; a cancelled request cannot be paid.</summary>
    public Result MarkPaid(DateTime now)
    {
        if (Status == PaymentStatus.Paid)
        {
            return Result.Success();
        }

        if (Status == PaymentStatus.Cancelled)
        {
            return Error.Conflict("payment.cancelled", "This payment was cancelled.");
        }

        Status = PaymentStatus.Paid;
        PaidOn = now;
        Raise(new PaymentReceived(this));

        return Result.Success();
    }

    /// <summary>Drops a QR that was not paid. A paid payment is never cancelled.</summary>
    public void Cancel()
    {
        if (Status == PaymentStatus.Paid)
        {
            throw new InvalidOperationException("A paid payment cannot be cancelled.");
        }

        Status = PaymentStatus.Cancelled;
    }
}
