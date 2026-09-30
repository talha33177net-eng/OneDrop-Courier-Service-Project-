using System.Text.Json;
using Domain.Common;
using Domain.Grouping;
using Domain.Notifications;
using Domain.Orders;
using Domain.Payments;

namespace Application.Notifications;

/// <summary>An order was placed in a delivery: text the customer which delivery it travels in and when it arrives.</summary>
public sealed record OrderPlacedMessage(long OrderId);

/// <summary>A delivery closed: text the customer the delivery day.</summary>
public sealed record DeliveryLockedMessage(long DeliveryGroupId);

/// <summary>A customer paid: text them a receipt.</summary>
public sealed record PaymentReceivedMessage(long PaymentId);

/// <summary>An order changed status: post it to its shop's webhook. The status is the one it moved to then.</summary>
public sealed record OrderStatusChangedMessage(long OrderId, long MerchantId, OrderStatus Status);

/// <summary>
/// Turns domain events into outbox rows. The row stores the contract's name and its JSON; the ids are read after the
/// entities are saved, which is why the save writes the outbox in a second step of the same transaction.
/// </summary>
public static class OutboxContracts
{
    public static OutboxMessage ToOutbox(IDomainEvent domainEvent)
    {
        return domainEvent switch
        {
            OrderPlacedInDelivery placed => Write(new OrderPlacedMessage(placed.Order.Id)),
            DeliveryGroupLocked locked => Write(new DeliveryLockedMessage(locked.Group.Id)),
            PaymentReceived received => Write(new PaymentReceivedMessage(received.Payment.Id)),
            OrderStatusChanged changed => Write(
                new OrderStatusChangedMessage(changed.Order.Id, changed.Order.MerchantId, changed.Status)),
            _ => throw new InvalidOperationException($"No outbox message is defined for {domainEvent.GetType().Name}.")
        };
    }

    private static OutboxMessage Write<TMessage>(TMessage message)
    {
        return OutboxMessage.Create(typeof(TMessage).Name, JsonSerializer.Serialize(message));
    }
}
