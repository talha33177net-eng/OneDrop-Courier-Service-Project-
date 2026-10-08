using Domain.Common;
using Domain.Merchants;

namespace Domain.Payments;

/// <summary>Stored as TINYINT. Never renumber a value that has been saved.</summary>
public enum PayoutStatus : byte
{
    /// <summary>Its lines are taken; the transfer has not been confirmed by the gateway yet.</summary>
    Pending = 1,

    Paid = 2,

    /// <summary>Taken back by the courier before it was sent: its lines wait for the next payout again.</summary>
    Cancelled = 3
}

/// <summary>
/// A payment to a merchant, with its invoice number (INV-100001): every ledger line of the merchant not yet paid out up
/// to <see cref="UpToDate"/>, when together they come to more than nothing. A merchant whose charges are more than its
/// cash is paid nothing and its lines wait for the next payout. Sent to the merchant's payout account through the payout
/// gateway; <see cref="PayoutStatus.Pending"/> until the gateway confirms it.
/// </summary>
public class Payout : TenantEntity, IMerchantOwned
{
    public const int MaxErrorLength = 300;

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

    /// <summary>The courier's adjustments it pays out: positive credited the merchant, negative charged it.</summary>
    public decimal AdjustmentsTotal { get; private set; }

    /// <summary>
    /// What the merchant receives: <see cref="CodTotal"/> less <see cref="ChargesTotal"/>, with <see cref="AdjustmentsTotal"/>.
    /// </summary>
    public decimal Amount { get; private set; }

    public PayoutMethod Method { get; private set; }

    /// <summary>Where the money goes: the merchant's account when the payout was made.</summary>
    public string Account { get; private set; } = "";

    public PayoutStatus Status { get; private set; }

    /// <summary>The gateway's id for the transfer, once sent.</summary>
    public string? GatewayReference { get; private set; }

    public DateTime? PaidOn { get; private set; }

    /// <summary>How many times the gateway refused or failed the transfer.</summary>
    public int FailedAttempts { get; private set; }

    /// <summary>Why the last try failed, as the gateway said it; null once paid.</summary>
    public string? LastError { get; private set; }

    /// <summary>When the gateway was last asked to send it, whatever the answer.</summary>
    public DateTime? LastTriedOn { get; private set; }

    /// <summary>Waiting to be sent, and the gateway has refused it at least once.</summary>
    public bool IsStuck => Status == PayoutStatus.Pending && FailedAttempts > 0;

    public byte[] RowVersion { get; private set; } = [];

    /// <summary>
    /// Pays out <paramref name="entries"/>, the merchant's lines not yet paid out up to <paramref name="upToDate"/>.
    /// Null, and nothing changes, when they come to nothing or less, the merchant has no payout account yet, or the
    /// courier holds its payouts: the lines wait for the next payout.
    /// </summary>
    public static Payout? Of(Merchant merchant, DateOnly upToDate, IReadOnlyCollection<LedgerEntry> entries)
    {
        if (entries.Any(entry => entry.MerchantId != merchant.Id || entry.IsPaidOut || entry.EntryDate > upToDate))
        {
            throw new InvalidOperationException("A payout takes the merchant's own lines not yet paid out, up to its day.");
        }

        var amount = entries.Sum(entry => entry.Amount);
        if (amount <= 0 || !merchant.HasPayoutAccount || merchant.ArePayoutsHeld)
        {
            return null;
        }

        var cod = entries.Where(entry => entry.Kind == LedgerEntryKind.Cod).Sum(entry => entry.Amount);
        var adjustments = entries.Where(entry => entry.Kind == LedgerEntryKind.Adjustment).Sum(entry => entry.Amount);
        var payout = new Payout
        {
            MerchantId = merchant.Id,
            UpToDate = upToDate,
            CodTotal = cod,
            ChargesTotal = cod + adjustments - amount,
            AdjustmentsTotal = adjustments,
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

        if (Status == PayoutStatus.Cancelled)
        {
            throw new InvalidOperationException($"Payout {Number} was cancelled; it cannot be paid.");
        }

        GatewayReference = gatewayReference;
        Status = PayoutStatus.Paid;
        PaidOn = now;
        LastTriedOn = now;
        LastError = null;
        Raise(new PayoutPaid(this));
    }

    /// <summary>The gateway did not send it: kept, with why, for the next run or the admin to send again.</summary>
    public void RecordFailure(string? reason, DateTime now)
    {
        if (Status != PayoutStatus.Pending)
        {
            throw new InvalidOperationException($"Payout {Number} is not waiting to be sent.");
        }

        var said = string.IsNullOrWhiteSpace(reason) ? "The gateway gave no reason." : reason.Trim();
        FailedAttempts++;
        LastError = said.Length > MaxErrorLength ? said[..MaxErrorLength] : said;
        LastTriedOn = now;
    }

    /// <summary>
    /// The courier takes back a payout not yet sent, for example one to an account the gateway keeps refusing:
    /// <paramref name="lines"/>, every line it took, wait for the next payout again, which goes to the merchant's
    /// account as it is then.
    /// </summary>
    public Result Cancel(IReadOnlyCollection<LedgerEntry> lines)
    {
        if (Status != PayoutStatus.Pending)
        {
            return Error.Conflict(
                "payout.cancel",
                Status == PayoutStatus.Paid ? $"{Number} was sent already; it cannot be cancelled." : $"{Number} is cancelled already.");
        }

        if (lines.Any(line => !ReferenceEquals(line.Payout, this) && (IsNew || line.PayoutId != Id)))
        {
            throw new InvalidOperationException("Cancelling a payout frees its own lines only.");
        }

        foreach (var line in lines)
        {
            line.Release();
        }

        Status = PayoutStatus.Cancelled;

        return Result.Success();
    }
}

/// <summary>The money for a payout has left: tell the merchant it is on the way to its account.</summary>
public sealed record PayoutPaid(Payout Payout) : IDomainEvent;
