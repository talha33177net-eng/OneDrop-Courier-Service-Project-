using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Application.Common;
using Domain.Common;
using Domain.Merchants;
using Domain.Parcels;
using Domain.Payments;

namespace Application.Payments.MerchantPayments;

/// <summary>
/// One ledger line as a statement shows it: the parcel, what kind of money and how much. An adjustment has no parcel and
/// says why in <see cref="Note"/>; it was either written by the courier or, when <see cref="PaidOnline"/>, is the
/// merchant's own online payment of what it owed.
/// </summary>
public sealed record StatementLine(string? TrackingCode, DateOnly Date, LedgerEntryKind Kind, decimal Amount, string? Note = null)
{
    public bool PaidOnline { get; init; }
}

/// <summary>A payout as a list shows it. Times are the tenant's.</summary>
public sealed record PayoutRow(
    string Number,
    string Merchant,
    DateOnly UpToDate,
    int Parcels,
    decimal CodTotal,
    decimal ChargesTotal,
    decimal Amount,
    PayoutMethod Method,
    string Account,
    PayoutStatus Status,
    DateTime Created,
    DateTime? PaidOn)
{
    /// <summary>The courier's adjustments in it: positive credited the merchant, negative charged it.</summary>
    public decimal AdjustmentsTotal { get; init; }

    /// <summary>How often the gateway refused it, and what it said the last time.</summary>
    public int FailedAttempts { get; init; }

    public string? LastError { get; init; }

    public DateTime? LastTriedOn { get; init; }

    public bool IsStuck => Status == PayoutStatus.Pending && FailedAttempts > 0;
}

/// <summary>What the courier holds for the merchant now, not yet paid out.</summary>
public sealed record Balance(decimal Cod, decimal Charges, int Parcels)
{
    /// <summary>
    /// The adjustment lines: positive credits the merchant, negative charges it. They include
    /// <see cref="PaidOnline"/>, the merchant's own online payments of what it owed.
    /// </summary>
    public decimal Adjustments { get; init; }

    public decimal PaidOnline { get; init; }

    public decimal Net => Cod - Charges + Adjustments;

    /// <summary>What the merchant owes the courier, when its charges come to more than its cash; otherwise ৳0.</summary>
    public decimal Owed => Net < 0 ? -Net : 0;
}

public sealed record MerchantPayments(
    Balance Unpaid,
    bool HasPayoutAccount,
    string? PayoutAccount,
    IReadOnlyList<StatementLine> UnpaidLines,
    IReadOnlyList<PayoutRow> Payouts)
{
    /// <summary>The balance's lines up to yesterday: the next payout run sends them by itself.</summary>
    public Balance Ready { get; init; } = new(0, 0, 0);

    /// <summary>The balance's lines of today: sent the morning after, or now when the merchant asks.</summary>
    public Balance Today { get; init; } = new(0, 0, 0);

    /// <summary>The cash on delivery of the parcels still on their way, not money yet.</summary>
    public decimal ToCollect { get; init; }

    public int ParcelsOnTheirWay { get; init; }

    /// <summary>Why the courier holds the account's payouts; null while they go out.</summary>
    public string? PayoutHold { get; init; }

    /// <summary>The merchant can be paid now: there is a payout account, payouts are not held and the balance is above nothing.</summary>
    public bool CanPayNow => HasPayoutAccount && PayoutHold is null && Unpaid.Net > 0;
}

public sealed record PayoutDetails(PayoutRow Payout, IReadOnlyList<StatementLine> Lines);

/// <summary>
/// The merchant's money: the balance not yet paid out with its lines, split into what the next run sends by itself and
/// what came in today, the cash still to collect on parcels on their way, the payouts made (invoices) and each payout's
/// lines. A merchant sees only its own (the merchant filter); courier staff see any merchant's payout.
/// </summary>
public class MerchantPaymentsHandler(IAppDbContext db, ITenantContext tenantContext, ICurrentUser currentUser, TimeProvider time)
{
    public static readonly Error NotFound = Error.NotFound("payout.notFound", "That payment was not found.");

    public async Task<MerchantPayments> GetAsync(CancellationToken cancellationToken = default)
    {
        var tenant = tenantContext.Require();
        var merchantId = currentUser.MerchantId ?? throw new InvalidOperationException("A merchant reads its payments.");
        var merchant = await db.Merchants.AsNoTracking().SingleAsync(m => m.Id == merchantId, cancellationToken);
        var unpaid = await LinesAsync(db.LedgerEntries.Where(e => e.PayoutId == null), cancellationToken);
        var payouts = await RowsAsync(db.Payouts.OrderByDescending(p => p.Id).Take(50), tenant, cancellationToken);
        var today = tenant.Today(time.GetUtcNow().UtcDateTime);
        var onTheirWay = await db.Parcels
            .Where(p => ParcelStatuses.ToDeliver.Contains(p.Status))
            .GroupBy(p => 1)
            .Select(g => new { Count = g.Count(), Cod = g.Sum(p => p.CodAmount) })
            .FirstOrDefaultAsync(cancellationToken);

        return new MerchantPayments(
            BalanceOf(unpaid),
            merchant.HasPayoutAccount,
            merchant.HasPayoutAccount
                ? $"{merchant.PayoutMethod!.Value.DisplayName()} " +
                  (merchant.PayoutMethod != PayoutMethod.Bank && PhoneNumber.Parse(merchant.PayoutAccount) is { IsSuccess: true } phone
                      ? phone.Value.Local
                      : merchant.PayoutAccount)
                : null,
            unpaid,
            payouts)
        {
            Ready = BalanceOf([.. unpaid.Where(line => line.Date < today)]),
            Today = BalanceOf([.. unpaid.Where(line => line.Date >= today)]),
            ToCollect = onTheirWay?.Cod ?? 0,
            ParcelsOnTheirWay = onTheirWay?.Count ?? 0,
            PayoutHold = merchant.PayoutHold
        };
    }

    public async Task<Result<PayoutDetails>> DetailsAsync(string? number, CancellationToken cancellationToken = default)
    {
        var tenant = tenantContext.Require();
        var payout = (await RowsAsync(db.Payouts.Where(p => p.Number == number), tenant, cancellationToken)).SingleOrDefault();
        if (payout is null)
        {
            return NotFound;
        }

        var lines = await LinesAsync(
            from line in db.LedgerEntries
            join p in db.Payouts on line.PayoutId equals p.Id
            where p.Number == number
            select line,
            cancellationToken);

        return new PayoutDetails(payout, lines);
    }

    public static Balance BalanceOf(IReadOnlyCollection<StatementLine> lines)
    {
        return new Balance(
            lines.Where(l => l.Kind == LedgerEntryKind.Cod).Sum(l => l.Amount),
            -lines.Where(l => l.Kind is not (LedgerEntryKind.Cod or LedgerEntryKind.Adjustment)).Sum(l => l.Amount),
            lines.Where(l => l.TrackingCode is not null).Select(l => l.TrackingCode).Distinct().Count())
        {
            Adjustments = lines.Where(l => l.Kind == LedgerEntryKind.Adjustment).Sum(l => l.Amount),
            PaidOnline = lines.Where(l => l.PaidOnline).Sum(l => l.Amount)
        };
    }

    internal async Task<IReadOnlyList<PayoutRow>> RowsAsync(IQueryable<Payout> payouts, TenantInfo tenant, CancellationToken cancellationToken)
    {
        var rows = await (
            from payout in payouts
            join merchant in db.Merchants on payout.MerchantId equals merchant.Id
            select new PayoutRow(
                payout.Number,
                merchant.Name,
                payout.UpToDate,
                db.LedgerEntries.Where(e => e.PayoutId == payout.Id && e.ParcelId != null).Select(e => e.ParcelId).Distinct().Count(),
                payout.CodTotal,
                payout.ChargesTotal,
                payout.Amount,
                payout.Method,
                payout.Account,
                payout.Status,
                payout.Created,
                payout.PaidOn)
            {
                AdjustmentsTotal = payout.AdjustmentsTotal,
                FailedAttempts = payout.FailedAttempts,
                LastError = payout.LastError,
                LastTriedOn = payout.LastTriedOn
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return [.. rows.Select(r => r with
            {
                Created = tenant.Local(r.Created),
                PaidOn = r.PaidOn is { } paid ? tenant.Local(paid) : null,
                LastTriedOn = r.LastTriedOn is { } tried ? tenant.Local(tried) : null
            })];
    }

    private async Task<IReadOnlyList<StatementLine>> LinesAsync(IQueryable<LedgerEntry> lines, CancellationToken cancellationToken)
    {
        return await (
            from line in lines
            join parcel in db.Parcels on line.ParcelId equals (long?)parcel.Id into found
            from parcel in found.DefaultIfEmpty()
            join payment in db.OnlinePayments on (long?)line.Id equals payment.LedgerEntryId into paid
            from payment in paid.DefaultIfEmpty()
            orderby line.EntryDate descending, parcel.TrackingCode, line.Kind
            select new StatementLine(parcel == null ? null : parcel.TrackingCode, line.EntryDate, line.Kind, line.Amount, line.Note)
            {
                PaidOnline = payment != null
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }
}

public static class PayoutNames
{
    public static string DisplayName(this PayoutMethod method)
    {
        return method switch
        {
            PayoutMethod.Bkash => "bKash",
            PayoutMethod.Nagad => "Nagad",
            _ => "Bank"
        };
    }

    public static string DisplayName(this LedgerEntryKind kind)
    {
        return kind switch
        {
            LedgerEntryKind.Cod => "Cash collected",
            LedgerEntryKind.DeliveryCharge => "Delivery charge",
            LedgerEntryKind.CodCharge => "COD charge",
            LedgerEntryKind.ReturnCharge => "Return charge",
            _ => "Adjustment"
        };
    }
}
