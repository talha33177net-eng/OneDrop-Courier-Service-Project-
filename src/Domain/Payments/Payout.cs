using Domain.Common;
using Domain.Merchants;

namespace Domain.Payments;

/// <summary>Stored as TINYINT. Never renumber a value that has been saved.</summary>
public enum PayoutStatus : byte
{
    /// <summary>Its lines are taken; the transfer has not been confirmed by the gateway yet.</summary>
    Pending = 1,

    Paid = 2
}

/// <summary>
/// A payment to a merchant, with its invoice number (INV-100001): every ledger line of the merchant not yet paid out up
/// to <see cref="UpToDate"/>, when together they come to more than nothing. A merchant whose charges are more than its
/// cash is paid nothing and its lines wait for the next payout. Sent to the merchant's payout account through the payout
/// gateway; <see cref="PayoutStatus.Pending"/> until the gateway confirms it.
/// </summary>
public class Payout : TenantEntity, IMerchantOwned
{
    private Payout()
    {
    }

    public long MerchantId { get; private set; }

    /// <summary>The invoice number (INV-100001), from the database sequence on insert.</summary>
    public string Number { get; private set; } = null!;

    /// <summary>The last of the tenant's days whose lines it pays out.</summary>
    public DateOnly UpToDate { get; private set; }

    /// <summary>The cash on delivery it pays out, before charges.</summary>
    public decimal CodTotal { get; private set; }

    /// <summary>The charges taken off, as a positive number.</summary>
    public decimal ChargesTotal { get; private set; }

    /// <summary>What the merchant receives: <see cref="CodTotal"/> less <see cref="ChargesTotal"/>.</summary>
    public decimal Amount { get; private set; }

    public PayoutMethod Method { get; private set; }

    /// <summary>Where the money goes: the merchant's account when the payout was made.</summary>
    public string Account { get; private set; } = "";

    public PayoutStatus Status { get; private set; }

    /// <summary>The gateway's id for the transfer, once sent.</summary>
    public string? GatewayReference { get; private set; }

    public DateTime? PaidOn { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    /// <summary>
    /// Pays out <paramref name="entries"/>, the merchant's lines not yet paid out up to <paramref name="upToDate"/>.
    /// Null, and nothing changes, when they come to nothing or less, or the merchant has no payout account yet: the lines
    /// wait for the next payout.
    /// </summary>
    public static Payout? Of(Merchant merchant, DateOnly upToDate, IReadOnlyCollection<LedgerEntry> entries)
    {
        if (entries.Any(entry => entry.MerchantId != merchant.Id || entry.IsPaidOut || entry.EntryDate > upToDate))
        {
            throw new InvalidOperationException("A payout takes the merchant's own lines not yet paid out, up to its day.");
        }

        var amount = entries.Sum(entry => entry.Amount);
        if (amount <= 0 || !merchant.HasPayoutAccount)
        {
            return null;
        }

        var cod = entries.Where(entry => entry.Kind == LedgerEntryKind.Cod).Sum(entry => entry.Amount);
        var payout = new Payout
        {
            MerchantId = merchant.Id,
            UpToDate = upToDate,
            CodTotal = cod,
            ChargesTotal = cod - amount,
            Amount = amount,
            Method = merchant.PayoutMethod!.Value,
            Account = merchant.PayoutAccount!,
            Status = PayoutStatus.Pending
        };
        foreach (var entry in entries)
        {
            entry.PayIn(payout);
        }

        return payout;
    }

    /// <summary>The gateway sent the money. Recording it again changes nothing.</summary>
    public void MarkPaid(string gatewayReference, DateTime now)
    {
        if (Status == PayoutStatus.Paid)
        {
            return;
        }

        GatewayReference = gatewayReference;
        Status = PayoutStatus.Paid;
        PaidOn = now;
    }
}
