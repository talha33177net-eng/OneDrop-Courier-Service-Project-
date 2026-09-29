using Domain.Common;

namespace Domain.Payments;

/// <summary>Stored as TINYINT. Never renumber a value that has been saved.</summary>
public enum SettlementStatus : byte
{
    /// <summary>Its lines are taken; the payout has not been confirmed by the gateway yet.</summary>
    Pending = 1,

    Paid = 2
}

/// <summary>
/// A payout to a shop: every ledger line of the shop not yet paid out up to <see cref="UpToDate"/>, when together they
/// come to more than nothing. A shop whose charges are more than its COD is paid nothing and its lines wait for the
/// next payout, which takes them off that day's COD. Sent to <see cref="Account"/> (the shop's bKash number) through the
/// payout gateway; <see cref="SettlementStatus.Pending"/> until the gateway confirms it.
/// </summary>
public class Settlement : TenantEntity, IMerchantOwned
{
    private Settlement()
    {
    }

    public long MerchantId { get; private set; }

    /// <summary>The last of the tenant's days whose lines it pays out.</summary>
    public DateOnly UpToDate { get; private set; }

    public decimal Amount { get; private set; }

    public SettlementStatus Status { get; private set; }

    /// <summary>Where the money goes: the shop's bKash number when the payout was made.</summary>
    public string Account { get; private set; } = "";

    /// <summary>The gateway's id for the transfer, once sent.</summary>
    public string? GatewayReference { get; private set; }

    public DateTime? PaidOn { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    /// <summary>
    /// Pays out <paramref name="entries"/>, the shop's lines not yet paid out up to <paramref name="upToDate"/>. Null,
    /// and nothing changes, when they come to nothing or less: the lines wait for the next payout.
    /// </summary>
    public static Settlement? Of(long merchantId, string account, DateOnly upToDate, IReadOnlyCollection<LedgerEntry> entries)
    {
        if (entries.Any(entry => entry.MerchantId != merchantId || entry.IsSettled || entry.EntryDate > upToDate))
        {
            throw new InvalidOperationException("A payout takes the shop's own lines not yet paid out, up to its day.");
        }

        var amount = entries.Sum(entry => entry.Amount);
        if (amount <= 0)
        {
            return null;
        }

        var settlement = new Settlement
        {
            MerchantId = merchantId,
            UpToDate = upToDate,
            Amount = amount,
            Status = SettlementStatus.Pending,
            Account = account
        };
        foreach (var entry in entries)
        {
            entry.SettleIn(settlement);
        }

        return settlement;
    }

    /// <summary>The gateway sent the money. Recording it again changes nothing.</summary>
    public void MarkPaid(string gatewayReference, DateTime now)
    {
        if (Status == SettlementStatus.Paid)
        {
            return;
        }

        GatewayReference = gatewayReference;
        Status = SettlementStatus.Paid;
        PaidOn = now;
    }
}
