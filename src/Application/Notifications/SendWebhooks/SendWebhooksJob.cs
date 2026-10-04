using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Application.Abstractions;
using Domain.Notifications;
using Domain.Parcels;

namespace Application.Notifications.SendWebhooks;

/// <summary>
/// Posts the tenant's due parcel status changes to their merchants' webhooks, oldest first, a batch per run; it runs
/// every few seconds, beside the texts, so a slow merchant server never holds up an SMS. A merchant with no webhook has
/// the message skipped. A merchant whose server fails is not called again in the same run: its failed message waits
/// longer before each retry (<see cref="OutboxMessage.MarkFailed"/>) and its other messages are tried on the next run, so
/// a retried status can arrive after a later one; the body's timestamp says which came first.
/// </summary>
public class SendWebhooksJob(
    IAppDbContext db,
    MerchantWebhooks webhooks,
    TimeProvider time,
    ILogger<SendWebhooksJob> logger)
    : ITenantJob
{
    public const int BatchSize = 50;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var due = await db.OutboxMessages
            .Where(m => m.Status == OutboxStatus.Pending && (m.NextAttemptOn == null || m.NextAttemptOn <= now) &&
                m.Type == nameof(ParcelStatusChangedMessage))
            .OrderBy(m => m.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
        if (due.Count == 0)
        {
            return;
        }

        var changes = due
            .Select(message =>
                (Message: message, Change: JsonSerializer.Deserialize<ParcelStatusChangedMessage>(message.Payload)!))
            .ToList();
        var merchantIds = changes.Select(c => c.Change.MerchantId).Distinct().ToList();
        var endpoints = await db.Merchants
            .Where(m => merchantIds.Contains(m.Id) && m.WebhookUrl != null)
            .Select(m => new { m.Id, Url = m.WebhookUrl!, Secret = m.WebhookSecret! })
            .ToDictionaryAsync(m => m.Id, cancellationToken);
        var parcelIds = changes
            .Where(c => endpoints.ContainsKey(c.Change.MerchantId))
            .Select(c => c.Change.ParcelId)
            .ToList();
        var parcels = await db.Parcels
            .Where(p => parcelIds.Contains(p.Id))
            .Select(p => new
            {
                p.Id,
                p.TrackingCode,
                p.MerchantReference,
                p.CodAmount,
                p.CollectedAmount,
                p.DeliveryCharge,
                p.HoldReason,
                p.ReturnReason
            })
            .ToDictionaryAsync(p => p.Id, cancellationToken);
        var failing = new HashSet<long>();

        foreach (var (message, change) in changes)
        {
            if (!endpoints.TryGetValue(change.MerchantId, out var endpoint))
            {
                message.MarkSkipped();
                await db.SaveChangesAsync(cancellationToken);
                continue;
            }

            if (failing.Contains(change.MerchantId))
            {
                continue;
            }

            var parcel = parcels[change.ParcelId];
            var body = MerchantWebhooks.Body(
                MerchantWebhooks.StatusChanged,
                message.Created,
                new ParcelStatusData(
                    parcel.TrackingCode,
                    parcel.MerchantReference,
                    change.Status,
                    parcel.CodAmount,
                    change.Status is ParcelStatus.Delivered or ParcelStatus.PartlyDelivered ? parcel.CollectedAmount : null,
                    parcel.DeliveryCharge,
                    change.Status switch
                    {
                        ParcelStatus.OnHold => parcel.HoldReason,
                        ParcelStatus.Returning => parcel.ReturnReason,
                        _ => null
                    }));
            var response = await webhooks.PostAsync(
                endpoint.Url,
                endpoint.Secret,
                $"msg_{message.Id}",
                body,
                cancellationToken);
            if (response.Delivered)
            {
                message.MarkSent(now);
            }
            else
            {
                message.MarkFailed(response.Describe, now);
                failing.Add(change.MerchantId);
                logger.LogWarning(
                    "Webhook {Id} to merchant {Merchant} failed ({Response}), attempt {Attempts}",
                    message.Id,
                    change.MerchantId,
                    response.Describe,
                    message.Attempts);
            }

            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
