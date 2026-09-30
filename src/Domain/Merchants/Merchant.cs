using Domain.Common;

namespace Domain.Merchants;

/// <summary>
/// An online shop that offers our delivery at checkout. Merchants pay nothing; they hand parcels to a scheduled
/// zone pickup and are settled for cash on delivery the next day.
/// </summary>
public class Merchant : TenantEntity, IArchivable
{
    public const int MaxWebhookUrlLength = 500;

    private Merchant()
    {
    }

    public Merchant(string name, long zoneId, string contactPhone, string? contactEmail)
    {
        Name = name.Trim();
        ZoneId = zoneId;
        ContactPhone = contactPhone.Trim();
        ContactEmail = contactEmail.NullIfBlank();
    }

    public string Name { get; private set; } = "";

    /// <summary>
    /// The merchant's home zone. Parcels are collected by the pickup route of each order's pickup point's zone,
    /// which is this one unless the merchant has a pickup point elsewhere.
    /// </summary>
    public long ZoneId { get; private set; }

    public string ContactPhone { get; private set; } = "";

    public string? ContactEmail { get; private set; }

    /// <summary>Where the shop's order status changes are posted. Null: the shop takes no webhooks.</summary>
    public string? WebhookUrl { get; private set; }

    /// <summary>
    /// The key the shop checks our webhook signatures with (<see cref="WebhookSignature"/>). Stored as it is: signing
    /// needs the secret itself, unlike an API key, which is only compared. Kept when the URL is removed.
    /// </summary>
    public string? WebhookSecret { get; private set; }

    public bool Archived { get; private set; }

    /// <summary>
    /// Sets where webhooks go, and gives the shop a secret the first time. The address must be https, so the order
    /// data and the signature cross the internet encrypted; plain http only to this machine (localhost), for testing.
    /// </summary>
    public Result SetWebhook(string? url)
    {
        var trimmed = url.NullIfBlank();
        if (trimmed is null || trimmed.Length > MaxWebhookUrlLength ||
            !Uri.TryCreate(trimmed, UriKind.Absolute, out var address) ||
            address.Scheme is not ("https" or "http") || address.UserInfo.Length > 0)
        {
            return Error.Validation(
                "merchant.webhook.url",
                $"Enter the full address of your webhook, starting with https://, at most {MaxWebhookUrlLength} " +
                "characters.");
        }

        if (address.Scheme == "http" && !address.IsLoopback &&
            !address.Host.EndsWith(".localhost", StringComparison.Ordinal))
        {
            return Error.Validation("merchant.webhook.https", "The webhook address must start with https://.");
        }

        WebhookUrl = address.AbsoluteUri;
        WebhookSecret ??= WebhookSignature.NewSecret();

        return Result.Success();
    }

    /// <summary>A new secret, for when the old one may have leaked. Signatures made with the old one stop verifying.</summary>
    public void NewWebhookSecret()
    {
        WebhookSecret = WebhookSignature.NewSecret();
    }

    public void RemoveWebhook()
    {
        WebhookUrl = null;
    }
}
