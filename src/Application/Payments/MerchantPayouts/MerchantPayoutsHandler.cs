using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Domain.Payments;

namespace Application.Payments.MerchantPayouts;

/// <summary>One ledger line of the shop: its order, what it is and the amount (negative for a charge).</summary>
public sealed record PayoutLine(DateOnly Day, string Order, LedgerEntryKind Kind, decimal Amount);

/// <summary>A payout made to the shop, with the lines it paid out.</summary>
public sealed record PayoutRow(
    DateOnly UpTo,
    decimal Amount,
    SettlementStatus Status,
    string Account,
    DateTime? PaidOn,
    IReadOnlyList<PayoutLine> Lines);

/// <summary>
/// The shop's money: <see cref="Waiting"/> are its lines not paid out yet (today's go out tomorrow; a charge left over
/// from a day with too little COD waits here too), <see cref="Balance"/> what they come to, and the latest payouts.
/// </summary>
public sealed record MerchantPayouts(decimal Balance, IReadOnlyList<PayoutLine> Waiting, IReadOnlyList<PayoutRow> Payouts);

/// <summary>
/// The signed-in shop's payouts. No WHERE MerchantId here: the merchant filter on the ledger, the payouts and the
/// orders adds it, so a shop only ever sees its own money.
/// </summary>
public class MerchantPayoutsHandler(IAppDbContext db)
{
    private const int LatestPayouts = 20;

    public async Task<MerchantPayouts> ListAsync(CancellationToken cancellationToken = default)
    {
        var payouts = await db.Settlements
            .OrderByDescending(s => s.UpToDate)
            .ThenByDescending(s => s.Id)
            .Take(LatestPayouts)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var payoutIds = payouts.Select(s => (long?)s.Id).ToList();
        var lines = await (
            from entry in db.LedgerEntries
            join order in db.Orders on entry.OrderId equals order.Id
            where entry.SettlementId == null || payoutIds.Contains(entry.SettlementId)
            orderby entry.EntryDate descending, order.Number descending, entry.Kind
            select new { entry.SettlementId, Line = new PayoutLine(entry.EntryDate, order.Number, entry.Kind, entry.Amount) })
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var waiting = lines.Where(line => line.SettlementId is null).Select(line => line.Line).ToList();

        return new MerchantPayouts(
            waiting.Sum(line => line.Amount),
            waiting,
            [
                .. payouts.Select(payout => new PayoutRow(
                    payout.UpToDate,
                    payout.Amount,
                    payout.Status,
                    payout.Account,
                    payout.PaidOn,
                    [.. lines.Where(line => line.SettlementId == payout.Id).Select(line => line.Line)]))
            ]);
    }
}
