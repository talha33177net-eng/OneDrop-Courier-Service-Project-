using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions;
using Domain.Merchants;
using Domain.Orders;

namespace Application.Notifications.SendWebhooks;

/// <summary>What a <see cref="MerchantWebhooks.StatusChanged"/> webhook says about an order; never its delivery.</summary>
public sealed record OrderStatusData(string Number, string? ExternalReference, OrderStatus Status);

/// <summary>
/// What a shop's webhook receives and how it is signed. The body is JSON <c>{ type, timestamp, data }</c>, the
/// timestamp being when it happened (UTC); the headers are those of Standard Webhooks (<see cref="WebhookSignature"/>),
/// signed at the moment of sending. The id stays the same on every retry, so a shop can ignore one it has already had.
/// </summary>
public class MerchantWebhooks(IWebhookSender sender, TimeProvider time)
{
    public const string StatusChanged = "order.status_changed";
    public const string Test = "webhook.test";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>The body; the timestamp to the whole second, as events are stored.</summary>
    public static string Body(string type, DateTime timestamp, object data)
    {
        timestamp = timestamp.AddTicks(-(timestamp.Ticks % TimeSpan.TicksPerSecond));

        return JsonSerializer.Serialize(new { type, timestamp, data }, Json);
    }

    public Task<WebhookResponse> PostAsync(
        string url,
        string secret,
        string id,
        string body,
        CancellationToken cancellationToken = default)
    {
        var timestamp = time.GetUtcNow().ToUnixTimeSeconds();
        var signature = WebhookSignature.Sign(secret, id, timestamp, body);

        return sender.PostAsync(new WebhookRequest(new Uri(url), id, timestamp, signature, body), cancellationToken);
    }
}
