using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Application.Abstractions;
using Domain.Payments;

namespace Infrastructure.Payments;

public sealed record FakePaymentRequest(
    string Reference,
    PaymentMethod Method,
    decimal Amount,
    string Currency,
    string Description,
    DateTime RequestedOn)
{
    public DateTime? PaidOn { get; init; }
}

/// <summary>What the fake gateway was asked for, so a demo or a test can pay a request by hand.</summary>
public class FakePaymentLog
{
    private readonly ConcurrentDictionary<string, FakePaymentRequest> requests = new();

    public IReadOnlyList<FakePaymentRequest> Recent => [.. requests.Values.OrderByDescending(r => r.RequestedOn)];

    public void Add(FakePaymentRequest request)
    {
        requests[request.Reference] = request;
    }

    public FakePaymentRequest? Find(string reference)
    {
        return requests.GetValueOrDefault(reference);
    }

    /// <summary>The customer pays: false when there is no such request.</summary>
    public bool Pay(string reference, DateTime now)
    {
        if (!requests.TryGetValue(reference, out var request))
        {
            return false;
        }

        requests[reference] = request with { PaidOn = request.PaidOn ?? now };

        return true;
    }
}

/// <summary>Adapter stand-in for bKash and Nagad: keeps each request in <see cref="FakePaymentLog"/>, unpaid until paid there.</summary>
public class FakePaymentGateway(FakePaymentLog log, TimeProvider time, ILogger<FakePaymentGateway> logger) : IPaymentGateway
{
    public Task<GatewayRequest> RequestAsync(
        PaymentMethod method,
        decimal amount,
        string currency,
        string description,
        CancellationToken cancellationToken = default)
    {
        var reference = $"FAKE-{method.ToString().ToUpperInvariant()}-{Guid.NewGuid():N}"[..30];
        log.Add(new FakePaymentRequest(reference, method, amount, currency, description, time.GetUtcNow().UtcDateTime));
        logger.LogInformation("{Method} payment {Reference} requested: {Amount} {Currency} for {Description}", method, reference, amount, currency, description);

        return Task.FromResult(new GatewayRequest(
            reference,
            $"https://pay.fake/{method.ToString().ToLowerInvariant()}/{reference}?amount={amount:0.##}&currency={currency}"));
    }

    public Task<bool> IsPaidAsync(PaymentMethod method, string reference, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(log.Find(reference) is { PaidOn: not null } request && request.Method == method);
    }
}
