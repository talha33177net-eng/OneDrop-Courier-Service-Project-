using System.Text.Json;
using Domain.Common;
using Domain.Notifications;
using Domain.Parcels;

namespace Application.Notifications;

/// <summary>A parcel changed status: post it to its merchant's webhook. The status is the one it moved to then.</summary>
public sealed record ParcelStatusChangedMessage(long ParcelId, long MerchantId, ParcelStatus Status);

/// <summary>A parcel reached a status the recipient is texted about (out for delivery, delivered).</summary>
public sealed record RecipientTextMessage(long ParcelId, ParcelStatus Status);

/// <summary>
/// Turns domain events into outbox rows. The row stores the contract's name and its JSON; the ids are read after the
/// entities are saved, which is why the save writes the outbox in a second step of the same transaction.
/// </summary>
public static class OutboxContracts
{
    /// <summary>Statuses the recipient hears about by SMS.</summary>
    public static readonly ParcelStatus[] Texted =
        [ParcelStatus.OutForDelivery, ParcelStatus.Delivered, ParcelStatus.PartlyDelivered];

    public static IEnumerable<OutboxMessage> ToOutbox(IDomainEvent domainEvent)
    {
        switch (domainEvent)
        {
            case ParcelStatusChanged changed:
                yield return Write(new ParcelStatusChangedMessage(changed.Parcel.Id, changed.Parcel.MerchantId, changed.Status));
                if (Texted.Contains(changed.Status))
                {
                    yield return Write(new RecipientTextMessage(changed.Parcel.Id, changed.Status));
                }

                break;

            default:
                throw new InvalidOperationException($"No outbox message is defined for {domainEvent.GetType().Name}.");
        }
    }

    private static OutboxMessage Write<TMessage>(TMessage message)
    {
        return OutboxMessage.Create(typeof(TMessage).Name, JsonSerializer.Serialize(message));
    }
}
