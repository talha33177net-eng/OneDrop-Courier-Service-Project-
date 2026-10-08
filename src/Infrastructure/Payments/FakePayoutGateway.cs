using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Application.Abstractions;

namespace Infrastructure.Payments;

public sealed record FakePayout(string Reference, string Key, string Account, decimal Amount, string Currency, DateTime SentOn);

/// <summary>
/// What the fake payout gateway sent, by idempotency key, so a demo or a test can see the transfers; and the accounts
/// it refuses, with the reason it gives, so a demo or a test can see a payout get stuck.
/// </summary>
public class FakePayoutLog
{
    private readonly ConcurrentDictionary<string, FakePayout> payouts = new();

    private readonly ConcurrentDictionary<string, string> refused = new();

    public IReadOnlyList<FakePayout> Recent => [.. payouts.Values.OrderByDescending(p => p.SentOn)];

    /// <summary>The accounts transfers to are refused, with the reason given.</summary>
    public IReadOnlyDictionary<string, string> Refused => refused;

    /// <summary>Records <paramref name="payout"/> unless one was sent with its key already, and returns the one kept.</summary>
    public FakePayout Add(FakePayout payout)
    {
        return payouts.GetOrAdd(payout.Key, payout);
    }

    public void Refuse(string account, string reason)
    {
        refused[account] = reason;
    }

    public void StopRefusing(string account)
    {
        refused.TryRemove(account, out _);
    }
}

/// <summary>
/// Adapter stand-in for bKash, Nagad and bank payouts: every transfer succeeds and is kept in <see cref="FakePayoutLog"/>,
/// except to an account the log was told to refuse.
/// </summary>
public class FakePayoutGateway(FakePayoutLog log, TimeProvider time, ILogger<FakePayoutGateway> logger) : IPayoutGateway
{
    public Task<string> SendAsync(
        string account,
        decimal amount,
        string currency,
        string key,
        CancellationToken cancellationToken = default)
    {
        if (log.Refused.TryGetValue(account, out var reason))
        {
            throw new PayoutRefusedException(reason);
        }

        var payout = log.Add(new FakePayout(
            $"FAKE-PAYOUT-{Guid.NewGuid():N}"[..30],
            key,
            account,
            amount,
            currency,
            time.GetUtcNow().UtcDateTime));
        logger.LogInformation("Payout {Reference} sent: {Amount} {Currency} to {Account} ({Key})", payout.Reference, payout.Amount, payout.Currency, payout.Account, payout.Key);

        return Task.FromResult(payout.Reference);
    }
}
