using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Application.Abstractions;
using Application.Common;
using Domain.Common;
using Domain.Payments;

namespace Application.Payments.RunPayouts;

/// <summary>
/// What a run did: payouts sent and the money in them, merchants whose balance waits (charges more than cash, no payout
/// account, or payouts held), and payouts the gateway refused, which stay for the next run.
/// </summary>
public sealed record PayoutRun(int Paid, decimal Amount, int Waiting, int Failed = 0);

/// <summary>A payout made on request: its invoice number, and whether the money left or the gateway refused it.</summary>
public sealed record PaidNow(string Number, bool Sent);

/// <summary>
/// Pays each merchant what the courier owes it: every ledger line not yet paid out up to yesterday (the tenant's day) is
/// one payout, the cash collected less the charges, sent by the payout gateway to the merchant's payout account. A
/// merchant whose charges come to more than its cash, who has no payout account yet, or whose payouts the courier holds,
/// is paid nothing and its lines wait for the next payout. Runs every hour, so yesterday is paid soon after the tenant's
/// midnight, and a payout the gateway refused is sent again within the hour; each refusal is kept on the payout with
/// what the gateway said. Two runs at once cannot pay the same line twice: the second one's save loses on the lines' row
/// versions. A merchant can also ask to be paid now (<see cref="PayNowAsync"/>), today's lines included, and the
/// courier's admin can send one payout again or cancel it.
/// </summary>
public class PayoutsJob(
    IAppDbContext db,
    ITenantContext tenantContext,
    IPayoutGateway gateway,
    TimeProvider time,
    ILogger<PayoutsJob> logger) : ITenantJob
{
    /// <summary>The hourly run's name in the scheduler.</summary>
    public const string RecurringId = "merchant-payouts";

    public static readonly Error NotFound = Error.NotFound("payout.notFound", "That payout was not found.");

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
            if (await SendAsync(payout, tenant, cancellationToken))
            {
                paid++;
                amount += payout.Amount;
            }
        }

        logger.LogInformation(
            "Paid out up to {UpTo}: {Paid} payouts, {Amount} sent, {Waiting} merchants waiting, {Failed} refused",
            upTo,
            paid,
            amount,
            waiting,
            pending.Count - paid);

        return new PayoutRun(paid, amount, waiting, pending.Count - paid);
    }

    /// <summary>
    /// Pays the merchant now instead of waiting for the next run, asked by the merchant or by the courier's admin: every
    /// line not yet paid out, today's too, becomes one payout, sent at once. Nothing is paid while they come to ৳0 or
    /// less, before there is a payout account, or while the courier holds the account's payouts; a transfer the gateway
    /// refuses is sent again by the next run.
    /// </summary>
    public async Task<Result<PaidNow>> PayNowAsync(long merchantId, CancellationToken cancellationToken = default)
    {
        var tenant = tenantContext.Require();
        var merchant = await db.Merchants.SingleOrDefaultAsync(m => m.Id == merchantId, cancellationToken);
        if (merchant is null)
        {
            return NotFound;
        }

        if (merchant.ArePayoutsHeld)
        {
            return Error.Conflict("payout.held", $"Payouts are on hold: {merchant.PayoutHold}");
        }

        if (!merchant.HasPayoutAccount)
        {
            return Error.Validation("payout.account", "There is no payout account yet, so there is nowhere to send the money.");
        }

        var lines = await db.LedgerEntries.Where(entry => entry.PayoutId == null && entry.MerchantId == merchantId).ToListAsync(cancellationToken);
        var payout = Payout.Of(merchant, tenant.Today(time.GetUtcNow().UtcDateTime), lines);
        if (payout is null)
        {
            return Error.Validation("payout.nothing", "There is nothing to pay yet: no cash is waiting, or the charges come to more than it.");
        }

        db.Payouts.Add(payout);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict("payout.changed", "A payout took these lines just now. See it among the payouts.");
        }

        return new PaidNow(payout.Number, await SendAsync(payout, tenant, cancellationToken));
    }

    /// <summary>
    /// The admin sends one payout again now instead of waiting for the next run. Its idempotency key is the same, so a
    /// transfer the gateway did make but failed to confirm is not made twice. Success means the money left.
    /// </summary>
    public async Task<Result> SendAgainAsync(string? number, CancellationToken cancellationToken = default)
    {
        var payout = await db.Payouts.SingleOrDefaultAsync(p => p.Number == number, cancellationToken);
        if (payout is null)
        {
            return NotFound;
        }

        if (payout.Status != PayoutStatus.Pending)
        {
            return Error.Conflict("payout.notPending", $"{payout.Number} is not waiting to be sent.");
        }

        return await SendAsync(payout, tenantContext.Require(), cancellationToken)
            ? Result.Success()
            : Error.Conflict("payout.failed", $"The gateway refused {payout.Number} again: {payout.LastError}");
    }

    /// <summary>
    /// The admin takes back a payout not yet sent: its lines wait for the next payout again, which goes to the merchant's
    /// account as it is then. For a payout the gateway keeps refusing, typically to an account that was wrong.
    /// </summary>
    public async Task<Result> CancelAsync(string? number, CancellationToken cancellationToken = default)
    {
        var payout = await db.Payouts.SingleOrDefaultAsync(p => p.Number == number, cancellationToken);
        if (payout is null)
        {
            return NotFound;
        }

        var lines = await db.LedgerEntries.Where(line => line.PayoutId == payout.Id).ToListAsync(cancellationToken);
        var cancelled = payout.Cancel(lines);
        if (cancelled.IsFailure)
        {
            return cancelled;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict("payout.changed", $"{payout.Number} changed just now, perhaps sent by the run. Look at it again.");
        }

        return Result.Success();
    }

    /// <summary>
    /// Sends one payout through the gateway; false when the gateway refuses it, which is kept on the payout with what the
    /// gateway said, and it stays pending for the next run.
    /// </summary>
    private async Task<bool> SendAsync(Payout payout, TenantInfo tenant, CancellationToken cancellationToken)
    {
        try
        {
            string reference;
            try
            {
                reference = await gateway.SendAsync(
                    payout.Account,
                    payout.Amount,
                    tenant.CurrencyCode,
                    $"{tenant.Slug}-payout-{payout.Id}",
                    cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Payout {Payout} to merchant {Merchant} refused; sent again on the next run", payout.Number, payout.MerchantId);
                payout.RecordFailure(exception.Message, time.GetUtcNow().UtcDateTime);
                await db.SaveChangesAsync(cancellationToken);

                return false;
            }

            payout.MarkPaid(reference, time.GetUtcNow().UtcDateTime);
            await db.SaveChangesAsync(cancellationToken);

            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The answer could not be saved; the next run asks the gateway again with the same key
            logger.LogWarning(exception, "Payout {Payout} to merchant {Merchant}: the gateway's answer was not saved", payout.Number, payout.MerchantId);

            return false;
        }
    }
}
