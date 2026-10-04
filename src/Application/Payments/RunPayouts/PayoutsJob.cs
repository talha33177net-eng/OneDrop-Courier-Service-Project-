using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Application.Abstractions;
using Application.Common;
using Domain.Payments;

namespace Application.Payments.RunPayouts;

/// <summary>What a run did: payouts made and the money sent, and merchants whose balance waits.</summary>
public sealed record PayoutRun(int Paid, decimal Amount, int Waiting);

/// <summary>
/// Pays each merchant what the courier owes it: every ledger line not yet paid out up to yesterday (the tenant's day) is
/// one payout, the cash collected less the charges, sent by the payout gateway to the merchant's payout account. A
/// merchant whose charges come to more than its cash, or who has no payout account yet, is paid nothing and its lines
/// wait for the next payout. Runs every hour, so yesterday is paid soon after the tenant's midnight, and a payout the
/// gateway failed is sent again within the hour. Two runs at once cannot pay the same line twice: the second one's save
/// loses on the lines' row versions.
/// </summary>
public class PayoutsJob(
    IAppDbContext db,
    ITenantContext tenantContext,
    IPayoutGateway gateway,
    TimeProvider time,
    ILogger<PayoutsJob> logger) : ITenantJob
{
    public Task RunAsync(CancellationToken cancellationToken)
    {
        return PayAsync(cancellationToken);
    }

    public async Task<PayoutRun> PayAsync(CancellationToken cancellationToken = default)
    {
        var tenant = tenantContext.Require();
        var upTo = tenant.Today(time.GetUtcNow().UtcDateTime).AddDays(-1);
        var due = await db.LedgerEntries
            .Where(entry => entry.PayoutId == null && entry.EntryDate <= upTo)
            .ToListAsync(cancellationToken);
        var merchantIds = due.Select(entry => entry.MerchantId).Distinct().ToList();
        var merchants = await db.Merchants
            .Where(merchant => merchantIds.Contains(merchant.Id))
            .ToDictionaryAsync(merchant => merchant.Id, cancellationToken);

        var waiting = 0;
        foreach (var lines in due.GroupBy(entry => entry.MerchantId))
        {
            var payout = Payout.Of(merchants[lines.Key], upTo, [.. lines]);
            if (payout is null)
            {
                waiting++;

                continue;
            }

            db.Payouts.Add(payout);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Another run paid these lines out meanwhile
                db.Entry(payout).State = EntityState.Detached;
                foreach (var entry in lines)
                {
                    db.Entry(entry).State = EntityState.Detached;
                }
            }
        }

        // Every payout not yet sent, including one a failed run left behind; the key stops a second transfer
        var pending = await db.Payouts
            .Where(p => p.Status == PayoutStatus.Pending)
            .OrderBy(p => p.Id)
            .ToListAsync(cancellationToken);
        var paid = 0;
        var amount = 0m;
        foreach (var payout in pending)
        {
            try
            {
                var reference = await gateway.SendAsync(
                    payout.Account,
                    payout.Amount,
                    tenant.CurrencyCode,
                    $"{tenant.Slug}-payout-{payout.Id}",
                    cancellationToken);
                payout.MarkPaid(reference, time.GetUtcNow().UtcDateTime);
                await db.SaveChangesAsync(cancellationToken);
                paid++;
                amount += payout.Amount;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Payout {Payout} to merchant {Merchant} failed; sent again on the next run", payout.Number, payout.MerchantId);
            }
        }

        logger.LogInformation("Paid out up to {UpTo}: {Paid} payouts, {Amount} sent, {Waiting} merchants waiting", upTo, paid, amount, waiting);

        return new PayoutRun(paid, amount, waiting);
    }
}
