using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Application.Common;
using Domain.Common;
using Domain.Merchants;
using Domain.Payments;

namespace Application.Payments.MerchantPayments;

/// <summary>One ledger line as a statement shows it: the parcel, what kind of money and how much.</summary>
public sealed record StatementLine(string TrackingCode, DateOnly Date, LedgerEntryKind Kind, decimal Amount);

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
    DateTime? PaidOn);

/// <summary>What the courier holds for the merchant now, not yet paid out.</summary>
public sealed record Balance(decimal Cod, decimal Charges, int Parcels)
{
    public decimal Net => Cod - Charges;
}

public sealed record MerchantPayments(
    Balance Unpaid,
    bool HasPayoutAccount,
    string? PayoutAccount,
    IReadOnlyList<StatementLine> UnpaidLines,
    IReadOnlyList<PayoutRow> Payouts);

public sealed record PayoutDetails(PayoutRow Payout, IReadOnlyList<StatementLine> Lines);

/// <summary>
/// The merchant's money: the balance waiting for the next payout with its lines, the payouts made (invoices) and each
/// payout's lines. A merchant sees only its own (the merchant filter); courier staff see any merchant's payout.
/// </summary>
public class MerchantPaymentsHandler(IAppDbContext db, ITenantContext tenantContext, ICurrentUser currentUser)
{
    public static readonly Error NotFound = Error.NotFound("payout.notFound", "That payment was not found.");

    public async Task<MerchantPayments> GetAsync(CancellationToken cancellationToken = default)
    {
        var tenant = tenantContext.Require();
        var merchantId = currentUser.MerchantId ?? throw new InvalidOperationException("A merchant reads its payments.");
        var merchant = await db.Merchants.AsNoTracking().SingleAsync(m => m.Id == merchantId, cancellationToken);
        var unpaid = await LinesAsync(db.LedgerEntries.Where(e => e.PayoutId == null), cancellationToken);
        var payouts = await RowsAsync(db.Payouts.OrderByDescending(p => p.Id).Take(50), tenant, cancellationToken);

        return new MerchantPayments(
            BalanceOf(unpaid),
            merchant.HasPayoutAccount,
            merchant.HasPayoutAccount ? $"{merchant.PayoutMethod!.Value.DisplayName()} {merchant.PayoutAccount}" : null,
            unpaid,
            payouts);
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
            -lines.Where(l => l.Kind != LedgerEntryKind.Cod).Sum(l => l.Amount),
            lines.Select(l => l.TrackingCode).Distinct().Count());
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
                db.LedgerEntries.Where(e => e.PayoutId == payout.Id).Select(e => e.ParcelId).Distinct().Count(),
                payout.CodTotal,
                payout.ChargesTotal,
                payout.Amount,
                payout.Method,
                payout.Account,
                payout.Status,
                payout.Created,
                payout.PaidOn))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return [.. rows.Select(r => r with { Created = tenant.Local(r.Created), PaidOn = r.PaidOn is { } paid ? tenant.Local(paid) : null })];
    }

    private async Task<IReadOnlyList<StatementLine>> LinesAsync(IQueryable<LedgerEntry> lines, CancellationToken cancellationToken)
    {
        return await (
            from line in lines
            join parcel in db.Parcels on line.ParcelId equals parcel.Id
            orderby line.EntryDate descending, parcel.TrackingCode, line.Kind
            select new StatementLine(parcel.TrackingCode, line.EntryDate, line.Kind, line.Amount))
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
            _ => "Return charge"
        };
    }
}
