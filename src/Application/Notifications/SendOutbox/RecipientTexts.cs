using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Domain.Common;
using Domain.Notifications;
using Domain.Parcels;

namespace Application.Notifications.SendOutbox;

/// <summary>
/// Writes and sends the recipient's SMS for an outbox message, from the data as it is when sent: a tracking link when
/// booked, the rider and the cash to keep ready when the parcel is out for delivery, a thank-you when it is delivered.
/// A message for a status the parcel has since left is dropped (sent with nothing to say).
/// </summary>
public class RecipientTexts(IAppDbContext db, ITenantContext tenantContext, ISmsSender sms, ITrackingLinks links)
{
    /// <summary>The outbox message types these texts handle; the texts job takes only these.</summary>
    public static readonly string[] Types = [nameof(RecipientTextMessage)];

    public async Task SendAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        var tenant = tenantContext.Tenant ?? throw new InvalidOperationException("Texts need a tenant.");
        var payload = JsonSerializer.Deserialize<RecipientTextMessage>(message.Payload)
            ?? throw new InvalidOperationException($"Outbox message {message.Id} has no payload.");

        var parcel = await (
            from p in db.Parcels
            join merchant in db.Merchants on p.MerchantId equals merchant.Id
            where p.Id == payload.ParcelId
            select new
            {
                p.TrackingCode,
                p.RecipientPhone,
                p.Status,
                p.CodAmount,
                p.CollectedAmount,
                Merchant = merchant.Name,
                Rider = db.Riders.Where(r => r.Id == p.RiderId).Select(r => new { r.Name, r.Phone }).FirstOrDefault()
            })
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException($"Parcel {payload.ParcelId} of outbox message {message.Id} is gone.");

        var text = payload.Status switch
        {
            ParcelStatus.Pending when parcel.Status == ParcelStatus.Pending =>
                $"Your parcel {parcel.TrackingCode} from {parcel.Merchant} has been booked with {tenant.Name}." +
                $" Track: {links.Track(parcel.TrackingCode)}",
            ParcelStatus.OutForDelivery when parcel.Status == ParcelStatus.OutForDelivery =>
                $"Your parcel {parcel.TrackingCode} from {parcel.Merchant} is out for delivery today" +
                (parcel.Rider is null ? "." : $" with {parcel.Rider.Name}, {PhoneNumber.Parse(parcel.Rider.Phone).Value.Local}.") +
                (parcel.CodAmount > 0 ? $" Please keep ৳{parcel.CodAmount:N0} ready." : "") +
                $" Track: {links.Track(parcel.TrackingCode)}",
            ParcelStatus.Delivered or ParcelStatus.PartlyDelivered =>
                $"Your parcel {parcel.TrackingCode} from {parcel.Merchant} has been delivered" +
                (parcel.CollectedAmount > 0 ? $", ৳{parcel.CollectedAmount:N0} paid." : ".") +
                $" Thank you for choosing {tenant.Name}. Help: {tenant.SupportPhone}",
            _ => null
        };
        if (text is null)
        {
            return;
        }

        await sms.SendAsync(PhoneNumber.Parse(parcel.RecipientPhone).Value, tenant.SmsSenderName, text, cancellationToken);
    }
}
