using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Application.Grouping.CombineDeliveries;
using Domain.Customers;
using Domain.Grouping;
using Domain.Notifications;
using Domain.Orders;
using Domain.Payments;

namespace Application.Notifications.SendOutbox;

/// <summary>
/// Writes and sends the customer's SMS for one outbox message. The text is built from the data as it is now, so a
/// retried message never says anything stale about the delivery day. Dates are in the tenant's time zone.
/// </summary>
public class CustomerTexts(
    IAppDbContext db,
    ITenantContext tenantContext,
    ICustomerLinks links,
    ISmsSender sms,
    CombineDeliveriesHandler combine)
{
    /// <summary>The outbox messages that are texts; the others are for someone else (<c>SendWebhooksJob</c>).</summary>
    public static readonly string[] Types =
        [nameof(OrderPlacedMessage), nameof(DeliveryLockedMessage), nameof(PaymentReceivedMessage)];

    public async Task SendAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var (phone, text) = message.Type switch
        {
            nameof(OrderPlacedMessage) => await OrderPlacedAsync(Read<OrderPlacedMessage>(message), cancellationToken),
            nameof(DeliveryLockedMessage) => await DeliveryLockedAsync(Read<DeliveryLockedMessage>(message), cancellationToken),
            nameof(PaymentReceivedMessage) => await ReceiptAsync(Read<PaymentReceivedMessage>(message), cancellationToken),
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
            select new
            {
                Order = order.Number,
                Shop = merchant.Name,
                delivery.Number,
                delivery.Status,
                delivery.LocksAt,
                customer.Phone,
                order.CustomerStep,
                order.ConfirmedOn,
                order.CustomerToken,
                order.CustomerId
            })
            .AsNoTracking()
            .SingleAsync(cancellationToken);

        var deliveryDay = LocalDate(placed.LocksAt);

        // Two spellings of one address in one area: ask whether it is the same place, on the order's page
        var question = placed.CustomerToken is null
            ? null
            : (await combine.QuestionsAsync(placed.CustomerId, cancellationToken))
                .FirstOrDefault(q => q.IsAbout(placed.Number));
        var (other, otherAddress) = question is null ? ("", "")
            : question.Delivery == placed.Number ? (question.OtherDelivery, question.OtherAddress)
            : (question.Delivery, question.Address);
        if (placed.ConfirmedOn is null && placed.CustomerStep != CustomerStep.None)
        {
            // The order waits for the customer: ask, and say no more about it than the shop already knows
            var link = links.Order(placed.CustomerToken!);
            var also = question is null ? ""
                : $" Is it going to the same address as your delivery {other} ({otherAddress})? Answer on the same page.";

            return (placed.Phone, (placed.CustomerStep == CustomerStep.Confirm
                ? $"Is your {placed.Shop} order {placed.Order} correct? Confirm it and we deliver on " +
                    $"{Format(deliveryDay)}: {link}"
                : $"Your {placed.Shop} order {placed.Order} goes out once the OneDrop delivery fee is paid. " +
                    $"Pay it here and we deliver on {Format(deliveryDay)}: {link}") + also);
        }

        var text = placed.Status == DeliveryGroupStatus.Open
            ? $"Your {placed.Shop} order {placed.Order} is in OneDrop delivery {placed.Number}. Orders from other " +
                $"shops can join it until the end of {Format(deliveryDay.AddDays(-1))}; we deliver on {Format(deliveryDay)}."
            : $"Your {placed.Shop} order {placed.Order} is in OneDrop delivery {placed.Number}, arriving on " +
                $"{Format(deliveryDay)}.";
        if (question is not null)
        {
            text += $" Same address as your delivery {other} ({otherAddress})? Combine them for one delivery and one " +
                $"fee: {links.Order(placed.CustomerToken!)}";
        }

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

    /// <summary>
    /// The receipt for a payment at the door: the amount and how it was paid, the delivery fee, and each order handed
    /// over with the cash on delivery it collected for its shop.
    /// </summary>
    private async Task<(string Phone, string Text)> ReceiptAsync(
        PaymentReceivedMessage message,
        CancellationToken cancellationToken)
    {
        var paid = await (
            from payment in db.Payments
            join customer in db.Customers on payment.CustomerId equals customer.Id
            join delivery in db.DeliveryGroups on payment.DeliveryGroupId equals delivery.Id
            where payment.Id == message.PaymentId
            select new
            {
                payment.Method,
                payment.Fee,
                payment.Cod,
                payment.PaidOn,
                delivery.Number,
                customer.Phone,
                Orders = (
                    from stop in db.TripStops
                    join order in db.Orders on stop.DeliveryGroupId equals order.DeliveryGroupId
                    join merchant in db.Merchants on order.MerchantId equals merchant.Id
                    where stop.PaymentId == payment.Id && order.Status == OrderStatus.Delivered
                    select new { order.Number, Shop = merchant.Name, order.CodAmount })
                    .ToList()
            })
            .AsNoTracking()
            .SingleAsync(cancellationToken);

        var how = paid.Method == PaymentMethod.Cash ? "in cash" : $"by {paid.Method.DisplayName()}";
        var orders = paid.Orders
            .OrderBy(order => order.Shop)
            .ThenBy(order => order.Number)
            .Select(order => $"{order.Shop} {order.Number}" + (order.CodAmount > 0 ? $" ৳{order.CodAmount:N0}" : ""));
        var text = $"OneDrop receipt: ৳{paid.Fee + paid.Cod:N0} paid {how} on {Format(LocalDate(paid.PaidOn!.Value))} " +
            $"for delivery {paid.Number}. Delivery fee ৳{paid.Fee:N0}. {string.Join(", ", orders)}. Thank you.";

        return (paid.Phone, text);
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
