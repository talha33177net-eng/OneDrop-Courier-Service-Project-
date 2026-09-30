using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Application.Abstractions;
using Domain.Notifications;

namespace Application.Notifications.SendWebhooks;

/// <summary>
/// Posts the tenant's due order status changes to their shops' webhooks, oldest first, a batch per run; it runs every
/// few seconds, beside the texts, so a slow shop server never holds up an SMS. A shop with no webhook has the message
/// skipped. A shop whose server fails is not called again in the same run: its failed message waits longer before each
/// retry (<see cref="OutboxMessage.MarkFailed"/>) and its other messages are tried on the next run, so a retried status
/// can arrive after a later one; the body's timestamp says which came first.
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
                m.Type == nameof(OrderStatusChangedMessage))
            .OrderBy(m => m.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
        if (due.Count == 0)
        {
            return;
        }

        var changes = due
            .Select(message =>
                (Message: message, Change: JsonSerializer.Deserialize<OrderStatusChangedMessage>(message.Payload)!))
            .ToList();
        var merchantIds = changes.Select(c => c.Change.MerchantId).Distinct().ToList();
        var endpoints = await db.Merchants
            .Where(m => merchantIds.Contains(m.Id) && m.WebhookUrl != null)
            .Select(m => new { m.Id, Url = m.WebhookUrl!, Secret = m.WebhookSecret! })
            .ToDictionaryAsync(m => m.Id, cancellationToken);
        var orderIds = changes
            .Where(c => endpoints.ContainsKey(c.Change.MerchantId))
            .Select(c => c.Change.OrderId)
            .ToList();
        var orders = await db.Orders
            .Where(o => orderIds.Contains(o.Id))
            .Select(o => new { o.Id, o.Number, o.ExternalReference })
            .ToDictionaryAsync(o => o.Id, cancellationToken);
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

            var order = orders[change.OrderId];
            var body = MerchantWebhooks.Body(
                MerchantWebhooks.StatusChanged,
                message.Created,
                new OrderStatusData(order.Number, order.ExternalReference, change.Status));
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
