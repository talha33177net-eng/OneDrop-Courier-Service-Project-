namespace Application.Abstractions;

/// <summary>
/// Adapter for paying shops (bKash disbursement). The MVP ships a fake whose transfers are listed at /Dev/Payments; a
/// real gateway plugs in later.
/// </summary>
public interface IPayoutGateway
{
    /// <summary>
    /// Sends <paramref name="amount"/> to <paramref name="account"/> and returns the gateway's reference. Sending again
    /// with the same <paramref name="key"/> sends nothing more and returns the first transfer's reference, so a payout
    /// retried after a failure is never paid twice.
    /// </summary>
    Task<string> SendAsync(
        string account,
        decimal amount,
        string currency,
        string key,
        CancellationToken cancellationToken = default);
}
