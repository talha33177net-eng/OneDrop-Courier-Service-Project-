namespace Application.Abstractions;

/// <summary>One signed POST to a shop's webhook address (headers of the Standard Webhooks specification).</summary>
public sealed record WebhookRequest(Uri Url, string Id, long Timestamp, string Signature, string Body);

/// <summary>What the shop's server answered: its status code, or why there was no answer (timeout, refused).</summary>
public sealed record WebhookResponse(int? StatusCode, string? Error = null)
{
    public bool Delivered => StatusCode is >= 200 and < 300;

    public string Describe => Error ?? $"HTTP {StatusCode}";
}

/// <summary>Posts webhooks to shops' servers. A shop server's failure is not thrown: it comes back as the response.</summary>
public interface IWebhookSender
{
    Task<WebhookResponse> PostAsync(WebhookRequest request, CancellationToken cancellationToken = default);
}
