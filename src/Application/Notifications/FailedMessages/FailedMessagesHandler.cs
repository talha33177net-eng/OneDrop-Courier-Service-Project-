using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Application.Notifications.SendOutbox;
using Domain.Common;
using Domain.Notifications;
using Domain.Orders;

namespace Application.Notifications.FailedMessages;

/// <summary>
/// One text or webhook that has not gone out: what it was about (an order or delivery number), who it was for (the
/// customer's phone or the shop), how often it was tried and the last error. <see cref="Created"/> is when it happened,
/// in the operator's time zone. <see cref="GivenUp"/> once every attempt has failed; otherwise it is still being retried.
/// </summary>
public sealed record FailedMessage(
    long Id,
    bool Text,
    string About,
    string To,
    DateTime Created,
    int Attempts,
    bool GivenUp,
    string? LastError);

/// <summary>
/// The operator's texts and webhooks that failed: given up after every attempt, or still being retried after a
/// failure, newest first. A given-up message can be sent again once the cause is fixed.
/// </summary>
public class FailedMessagesHandler(IAppDbContext db, ITenantContext tenantContext)
{
    public const int Shown = 100;

    public static readonly Error NotFound = Error.NotFound("outbox.notFound", "That message was not found.");

    public async Task<IReadOnlyList<FailedMessage>> ListAsync(CancellationToken cancellationToken = default)
    {
        var tenant = tenantContext.Tenant ?? throw new InvalidOperationException("Messages need a tenant.");
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(tenant.TimeZone);
        var messages = await db.OutboxMessages
            .Where(m => m.Status == OutboxStatus.Failed || (m.Status == OutboxStatus.Pending && m.Attempts > 0))
            .OrderByDescending(m => m.Id)
            .Take(Shown)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var payloads = messages.ToDictionary(m => m.Id, m => JsonSerializer.Deserialize<Ids>(m.Payload)!);
        var orderIds = payloads.Values.Select(p => p.OrderId).OfType<long>().Distinct().ToList();
        var deliveryIds = payloads.Values.Select(p => p.DeliveryGroupId).OfType<long>().Distinct().ToList();
        var paymentIds = payloads.Values.Select(p => p.PaymentId).OfType<long>().Distinct().ToList();

        // Read across the operator's shops: these are the operator's own messages
        var orders = await (
            from order in db.Orders.IgnoreQueryFilters([QueryFilters.Merchant])
            join customer in db.Customers on order.CustomerId equals customer.Id
            join merchant in db.Merchants.IgnoreQueryFilters([QueryFilters.Merchant]) on order.MerchantId equals merchant.Id
            where orderIds.Contains(order.Id)
            select new { order.Id, order.Number, customer.Phone, Shop = merchant.Name })
            .ToDictionaryAsync(o => o.Id, cancellationToken);
        var deliveries = await (
            from delivery in db.DeliveryGroups
            join customer in db.Customers on delivery.CustomerId equals customer.Id
            where deliveryIds.Contains(delivery.Id)
            select new { delivery.Id, delivery.Number, customer.Phone })
            .ToDictionaryAsync(d => d.Id, cancellationToken);
        var payments = await (
            from payment in db.Payments
            join delivery in db.DeliveryGroups on payment.DeliveryGroupId equals delivery.Id
            join customer in db.Customers on payment.CustomerId equals customer.Id
            where paymentIds.Contains(payment.Id)
            select new { payment.Id, delivery.Number, customer.Phone })
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        return [.. messages.Select(Describe)];

        FailedMessage Describe(OutboxMessage message)
        {
            var ids = payloads[message.Id];
            var text = CustomerTexts.Types.Contains(message.Type);
            var (about, to) = message.Type switch
            {
                nameof(OrderPlacedMessage) when orders.TryGetValue(ids.OrderId!.Value, out var o) =>
                    ($"Order {o.Number} placed", o.Phone),
                nameof(OrderStatusChangedMessage) when orders.TryGetValue(ids.OrderId!.Value, out var o) =>
                    ($"Order {o.Number} status {JsonNamingPolicy.CamelCase.ConvertName(ids.Status.ToString()!)}", o.Shop),
                nameof(DeliveryLockedMessage) when deliveries.TryGetValue(ids.DeliveryGroupId!.Value, out var d) =>
                    ($"Delivery {d.Number} closed", d.Phone),
                nameof(PaymentReceivedMessage) when payments.TryGetValue(ids.PaymentId!.Value, out var p) =>
                    ($"Receipt for delivery {p.Number}", p.Phone),
                _ => (message.Type, "")
            };

            return new FailedMessage(
                message.Id,
                text,
                about,
                to,
                TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(message.Created, DateTimeKind.Utc), timeZone),
                message.Attempts,
                message.Status == OutboxStatus.Failed,
                message.LastError);
        }
    }

    /// <summary>Puts a given-up message back in the queue; the senders pick it up within seconds.</summary>
    public async Task<Result> SendAgainAsync(long id, CancellationToken cancellationToken = default)
    {
        var message = await db.OutboxMessages.SingleOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (message is null)
        {
            return NotFound;
        }

        var again = message.SendAgain();
        if (again.IsSuccess)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return again;
    }

    /// <summary>Every id a message contract carries; each message fills the ones it has.</summary>
    private sealed record Ids(long? OrderId, long? DeliveryGroupId, long? PaymentId, OrderStatus? Status);
}
