using Domain.Common;
using Domain.Orders;

namespace Domain.Payments;

/// <summary>What a ledger entry records. Stored as TINYINT. Never renumber a value that has been saved.</summary>
public enum LedgerEntryKind : byte
{
    /// <summary>Cash on delivery the customer paid for the shop's order: owed to the shop.</summary>
    Cod = 1,

    /// <summary>The shop's order came back (refused, or nobody home at the re-attempt): the shop pays the return.</summary>
    ReturnCharge = 2,

    /// <summary>The shop had not handed the order over when its delivery left: the shop pays for the second trip.</summary>
    LateHandoverFee = 3
}

/// <summary>
/// One line of what the operator owes a shop, for one of its orders: <see cref="Amount"/> is positive when owed to the
/// shop (its COD) and negative when the shop owes it (a charge). <see cref="EntryDate"/> is the tenant's day it
/// belongs to. A line is paid out once, by the <see cref="Settlement"/> that takes it; until then it counts towards the
/// shop's next payout, so a charge the day's COD does not cover is carried forward. One line of each kind per order.
/// </summary>
public class LedgerEntry : TenantEntity, IMerchantOwned
{
    private LedgerEntry()
    {
    }

    public long MerchantId { get; private set; }

    public long OrderId { get; private set; }

    public LedgerEntryKind Kind { get; private set; }

    public decimal Amount { get; private set; }

    public DateOnly EntryDate { get; private set; }

    /// <summary>The customer's payment the COD came in with; set for <see cref="LedgerEntryKind.Cod"/>.</summary>
    public long? PaymentId { get; private set; }

    /// <summary>Set with <see cref="Cod"/> so a payment made at the door is saved with its lines.</summary>
    public Payment? Payment { get; private set; }

    /// <summary>The payout that settled the line; null while it waits for the next one.</summary>
    public long? SettlementId { get; private set; }

    public Settlement? Settlement { get; private set; }

    /// <summary>Two settlements taking the same line at once: the second one's save fails.</summary>
    public byte[] RowVersion { get; private set; } = [];

    public bool IsSettled => SettlementId is not null || Settlement is not null;

    /// <summary>The COD of an order handed to the customer, from the payment it was paid in.</summary>
    public static LedgerEntry Cod(Order order, Payment payment, DateOnly entryDate)
    {
        if (order.Status != OrderStatus.Delivered || payment.Status != PaymentStatus.Paid)
        {
            throw new InvalidOperationException("COD is owed to a shop for a delivered order with a paid payment.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(order.CodAmount);

        return new LedgerEntry
        {
            MerchantId = order.MerchantId,
            OrderId = order.Id,
            Kind = LedgerEntryKind.Cod,
            Amount = order.CodAmount,
            EntryDate = entryDate,
            Payment = payment
        };
    }

    /// <summary>The tenant's return charge for an order going back to its shop.</summary>
    public static LedgerEntry ReturnCharge(Order order, decimal charge, DateOnly entryDate)
    {
        if (order.Status != OrderStatus.Refused)
        {
            throw new InvalidOperationException("A return is charged for an order going back to its shop.");
        }

        return Charge(order, LedgerEntryKind.ReturnCharge, charge, entryDate);
    }

    /// <summary>The tenant's late-handover fee for an order a rider had to leave behind because the shop was late.</summary>
    public static LedgerEntry LateHandoverFee(Order order, decimal fee, DateOnly entryDate)
    {
        if (order.ShopLateOn is null)
        {
            throw new InvalidOperationException("A late handover is charged for an order its shop had not handed over.");
        }

        return Charge(order, LedgerEntryKind.LateHandoverFee, fee, entryDate);
    }

    /// <summary>The payout <paramref name="settlement"/> takes the line. A line is settled once.</summary>
    internal void SettleIn(Settlement settlement)
    {
        if (IsSettled)
        {
            throw new InvalidOperationException("This ledger line is already paid out.");
        }

        Settlement = settlement;
    }

    private static LedgerEntry Charge(Order order, LedgerEntryKind kind, decimal charge, DateOnly entryDate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(charge);

        return new LedgerEntry
        {
            MerchantId = order.MerchantId,
            OrderId = order.Id,
            Kind = kind,
            Amount = -charge,
            EntryDate = entryDate
        };
    }
}
