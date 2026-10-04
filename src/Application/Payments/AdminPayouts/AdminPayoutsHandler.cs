using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Application.Common;
using Application.Payments.MerchantPayments;
using Domain.Payments;

namespace Application.Payments.AdminPayouts;

/// <summary>A merchant's balance not yet paid out, and whether it can be paid.</summary>
public sealed record MerchantBalance(long MerchantId, string Merchant, decimal Cod, decimal Charges, bool HasPayoutAccount)
{
    public decimal Net => Cod - Charges;
}

public sealed record PayoutsOverview(
    IReadOnlyList<MerchantBalance> Owed,
    IReadOnlyList<PayoutRow> Payouts,
    decimal PaidThisMonth,
    decimal PendingTotal);

/// <summary>
/// The courier's payouts: what it owes each merchant now, and the payouts made, newest first. Courier admins only.
/// </summary>
public class AdminPayoutsHandler(
    IAppDbContext db,
    ITenantContext tenantContext,
    MerchantPaymentsHandler payments,
    TimeProvider time)
{
    public async Task<PayoutsOverview> GetAsync(PayoutStatus? status, CancellationToken cancellationToken = default)
    {
        var tenant = tenantContext.Require();
        var balances = await db.LedgerEntries
            .Where(line => line.PayoutId == null)
            .GroupBy(line => line.MerchantId)
            .Select(lines => new
            {
                MerchantId = lines.Key,
                Cod = lines.Sum(l => l.Kind == LedgerEntryKind.Cod ? l.Amount : 0),
                Charges = -lines.Sum(l => l.Kind != LedgerEntryKind.Cod ? l.Amount : 0)
            })
            .ToListAsync(cancellationToken);
        var merchantIds = balances.Select(b => b.MerchantId).ToList();
        var merchants = await db.Merchants
            .Where(m => merchantIds.Contains(m.Id))
            .Select(m => new { m.Id, m.Name, HasAccount = m.PayoutMethod != null })
            .ToDictionaryAsync(m => m.Id, cancellationToken);
        var owed = balances
            .Select(b =>
                new MerchantBalance(b.MerchantId, merchants[b.MerchantId].Name, b.Cod, b.Charges, merchants[b.MerchantId].HasAccount))
            .ToList();

        var payouts = db.Payouts.AsQueryable();
        if (status is { } only)
        {
            payouts = payouts.Where(p => p.Status == only);
        }

        var today = tenant.Today(time.GetUtcNow().UtcDateTime);
        var monthStart = tenant.StartUtc(new DateOnly(today.Year, today.Month, 1));
        var paidThisMonth = await db.Payouts
            .Where(p => p.Status == PayoutStatus.Paid && p.PaidOn >= monthStart)
            .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0;
        var pending = await db.Payouts
            .Where(p => p.Status == PayoutStatus.Pending)
            .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0;

        return new PayoutsOverview(
            [.. owed.OrderByDescending(m => m.Net)],
            await payments.RowsAsync(payouts.OrderByDescending(p => p.Id).Take(100), tenant, cancellationToken),
            paidThisMonth,
            pending);
    }
}
