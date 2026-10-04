using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Application.Notifications.SendWebhooks;
using Domain.Common;
using Domain.Merchants;

namespace Application.Merchants.Webhook;

public sealed record WebhookSettings(string MerchantName, string? Url, string? Secret);

/// <summary>
/// The signed-in shop's own webhook: where its parcel status changes go, the secret it checks them with, and a test
/// message sent at once so the shop sees what its server answers.
/// </summary>
public class MerchantWebhookHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    MerchantWebhooks webhooks,
    TimeProvider time)
{
    public async Task<WebhookSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        var merchant = await MerchantAsync(cancellationToken);

        return new WebhookSettings(merchant.Name, merchant.WebhookUrl, merchant.WebhookSecret);
    }

    public async Task<Result> SaveAsync(string? url, CancellationToken cancellationToken = default)
    {
        var merchant = await MerchantAsync(cancellationToken);
        var saved = merchant.SetWebhook(url);
        if (saved.IsSuccess)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return saved;
    }

    public async Task NewSecretAsync(CancellationToken cancellationToken = default)
    {
        (await MerchantAsync(cancellationToken)).NewWebhookSecret();
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAsync(CancellationToken cancellationToken = default)
    {
        (await MerchantAsync(cancellationToken)).RemoveWebhook();
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Posts a <see cref="MerchantWebhooks.Test"/> message, signed like every other, and says what came back.</summary>
    public async Task<Result<WebhookResponse>> SendTestAsync(CancellationToken cancellationToken = default)
    {
        var merchant = await MerchantAsync(cancellationToken);
        if (merchant.WebhookUrl is null)
        {
            return Error.Conflict("merchant.webhook.none", "Save your webhook address first.");
        }

        var body = MerchantWebhooks.Body(
            MerchantWebhooks.Test,
            time.GetUtcNow().UtcDateTime,
            new { shop = merchant.Name });

        return await webhooks.PostAsync(
            merchant.WebhookUrl,
            merchant.WebhookSecret!,
            $"msg_test_{Guid.NewGuid():N}",
            body,
            cancellationToken);
    }

    /// <summary>The shop signed in; the merchant filter already hides every other, the id says which is meant.</summary>
    private Task<Merchant> MerchantAsync(CancellationToken cancellationToken)
    {
        var merchantId = currentUser.MerchantId ?? throw new InvalidOperationException("Only a shop has a webhook.");

        return db.Merchants.SingleAsync(m => m.Id == merchantId, cancellationToken);
    }
}
