using Domain.Payments;

namespace Application.Abstractions;

/// <summary>A payment the gateway is waiting for: its reference and the link the customer pays through (the QR).</summary>
public sealed record GatewayRequest(string Reference, string Link);

/// <summary>
/// Adapter for the mobile wallets (bKash, Nagad). The MVP ships a fake that a developer pays by hand at /Dev/Payments;
/// a real gateway plugs in later.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Asks the wallet for a payment of <paramref name="amount"/>; the customer pays through the link.</summary>
    Task<GatewayRequest> RequestAsync(
        PaymentMethod method,
        decimal amount,
        string currency,
        string description,
        CancellationToken cancellationToken = default);

    /// <summary>True once the customer has paid the request.</summary>
    Task<bool> IsPaidAsync(PaymentMethod method, string reference, CancellationToken cancellationToken = default);
}
