using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Application.Abstractions;
using Application.Delivery;
using Domain.Payments;

namespace Application.Payments.SettleMerchants;

/// <summary>What a run did: payouts made and the money sent, and shops whose charges left nothing to pay.</summary>
public sealed record SettlementRun(int Paid, decimal Amount, int CarriedForward);

/// <summary>
/// Pays each shop what the operator owes it: every ledger line not yet paid out up to yesterday (the tenant's day) is
/// one payout, the COD less any charges, sent by the payout gateway. A shop whose charges come to more than its COD is
/// paid nothing and its lines wait for the next payout. Runs every hour, so yesterday is paid out soon after the
/// tenant's midnight, whatever its time zone, and a payout the gateway failed is sent again within the hour. Two runs at
/// once cannot pay the same line twice: the second one's save loses on the lines' row versions.
/// </summary>
public class SettleMerchantsJob(
    IAppDbContext db,
    ITenantContext tenantContext,
    IPayoutGateway gateway,
    TimeProvider time,
    ILogger<SettleMerchantsJob> logger) : ITenantJob
{
    public Task RunAsync(CancellationToken cancellationToken)
    {
        return SettleAsync(cancellationToken);
    }

    public async Task<SettlementRun> SettleAsync(CancellationToken cancellationToken = default)
    {
        var tenant = tenantContext.Tenant ?? throw new InvalidOperationException("Settling needs a tenant.");
        var now = time.GetUtcNow().UtcDateTime;
        var upTo = TimeZoneInfo.FindSystemTimeZoneById(tenant.TimeZone).LocalDay(now).AddDays(-1);
        var due = await db.LedgerEntries
            .Where(entry => entry.SettlementId == null && entry.EntryDate <= upTo)
            .ToListAsync(cancellationToken);
        var merchantIds = due.Select(entry => entry.MerchantId).Distinct().ToList();
        var accounts = await db.Merchants
            .Where(merchant => merchantIds.Contains(merchant.Id))
            .ToDictionaryAsync(merchant => merchant.Id, merchant => merchant.ContactPhone, cancellationToken);

        var carried = 0;
        foreach (var shop in due.GroupBy(entry => entry.MerchantId))
        {
            var settlement = Settlement.Of(shop.Key, accounts[shop.Key], upTo, [.. shop]);
            if (settlement is null)
            {
                carried++;

                continue;
            }

            db.Settlements.Add(settlement);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Another run paid these lines out meanwhile
                db.Entry(settlement).State = EntityState.Detached;
                foreach (var entry in shop)
                {
                    db.Entry(entry).State = EntityState.Detached;
                }
            }
        }

        // Every payout not yet sent, including one a failed run left behind; the key stops a second transfer
        var pending = await db.Settlements
            .Where(s => s.Status == SettlementStatus.Pending)
            .OrderBy(s => s.Id)
            .ToListAsync(cancellationToken);
        var paid = 0;
        var amount = 0m;
        foreach (var settlement in pending)
        {
            try
            {
                var reference = await gateway.SendAsync(
                    settlement.Account,
                    settlement.Amount,
                    tenant.CurrencyCode,
                    $"{tenant.Slug}-settlement-{settlement.Id}",
                    cancellationToken);
                settlement.MarkPaid(reference, time.GetUtcNow().UtcDateTime);
                await db.SaveChangesAsync(cancellationToken);
                paid++;
                amount += settlement.Amount;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Payout {Settlement} to merchant {Merchant} failed; sent again on the next run", settlement.Id, settlement.MerchantId);
            }
        }

        logger.LogInformation("Settled up to {UpTo}: {Paid} payouts, {Amount} sent, {Carried} carried forward", upTo, paid, amount, carried);

        return new SettlementRun(paid, amount, carried);
    }
}
