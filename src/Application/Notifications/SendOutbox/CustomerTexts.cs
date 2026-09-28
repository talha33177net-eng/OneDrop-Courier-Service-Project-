using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Domain.Customers;
using Domain.Grouping;
using Domain.Notifications;

namespace Application.Notifications.SendOutbox;

/// <summary>
/// Writes and sends the customer's SMS for one outbox message. The text is built from the data as it is now, so a
/// retried message never says anything stale about the delivery day. Dates are in the tenant's time zone.
/// </summary>
public class CustomerTexts(IAppDbContext db, ITenantContext tenantContext, ISmsSender sms)
{
    public async Task SendAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var (phone, text) = message.Type switch
        {
            nameof(OrderPlacedMessage) => await OrderPlacedAsync(Read<OrderPlacedMessage>(message), cancellationToken),
            nameof(DeliveryLockedMessage) => await DeliveryLockedAsync(Read<DeliveryLockedMessage>(message), cancellationToken),
            _ => throw new InvalidOperationException($"No text is written for outbox message type {message.Type}.")
        };

        await sms.SendAsync(PhoneNumber.Parse(phone).Value, Tenant.SmsSenderName, text, cancellationToken);
    }

    private TenantInfo Tenant => tenantContext.Tenant ?? throw new InvalidOperationException("Texts need a tenant.");

    private async Task<(string Phone, string Text)> OrderPlacedAsync(
        OrderPlacedMessage message,
        CancellationToken cancellationToken)
    {
        var placed = await (
            from order in db.Orders
            join merchant in db.Merchants on order.MerchantId equals merchant.Id
            join delivery in db.DeliveryGroups on order.DeliveryGroupId equals delivery.Id
            join customer in db.Customers on order.CustomerId equals customer.Id
            where order.Id == message.OrderId
            select new { Order = order.Number, Shop = merchant.Name, delivery.Number, delivery.Status, delivery.LocksAt, customer.Phone })
            .AsNoTracking()
            .SingleAsync(cancellationToken);

        var deliveryDay = LocalDate(placed.LocksAt);
        var text = placed.Status == DeliveryGroupStatus.Open
            ? $"Your {placed.Shop} order {placed.Order} is in OneDrop delivery {placed.Number}. Orders from other " +
                $"shops can join it until the end of {Format(deliveryDay.AddDays(-1))}; we deliver on {Format(deliveryDay)}."
            : $"Your {placed.Shop} order {placed.Order} is in OneDrop delivery {placed.Number}, arriving on " +
                $"{Format(deliveryDay)}.";

        return (placed.Phone, text);
    }

    private async Task<(string Phone, string Text)> DeliveryLockedAsync(
        DeliveryLockedMessage message,
        CancellationToken cancellationToken)
    {
        var locked = await (
            from delivery in db.DeliveryGroups
            join customer in db.Customers on delivery.CustomerId equals customer.Id
            where delivery.Id == message.DeliveryGroupId
            select new
            {
                delivery.Number,
                delivery.LocksAt,
                customer.Phone,
                Shops = (
                    from order in db.Orders
                    join merchant in db.Merchants on order.MerchantId equals merchant.Id
                    where order.DeliveryGroupId == delivery.Id
                    select merchant.Name)
                    .Distinct()
                    .ToList()
            })
            .AsNoTracking()
            .SingleAsync(cancellationToken);

        var text = $"Your OneDrop delivery {locked.Number} is closed. Your orders from " +
            $"{string.Join(", ", locked.Shops.Order())} arrive together on {Format(LocalDate(locked.LocksAt))}.";

        return (locked.Phone, text);
    }

    private static TMessage Read<TMessage>(OutboxMessage message)
    {
        return JsonSerializer.Deserialize<TMessage>(message.Payload)
            ?? throw new InvalidOperationException($"Outbox message {message.Id} has no payload.");
    }

    private DateOnly LocalDate(DateTime utc)
    {
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById(Tenant.TimeZone)));
    }

    private static string Format(DateOnly day)
    {
        return day.ToString("ddd d MMM", CultureInfo.InvariantCulture);
    }
}
