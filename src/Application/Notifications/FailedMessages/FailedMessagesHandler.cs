using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Application.Common;
using Application.Notifications.SendOutbox;
using Domain.Common;
using Domain.Notifications;
using Domain.Parcels;

namespace Application.Notifications.FailedMessages;

/// <summary>
/// One text or webhook that has not gone out: what it was about (a parcel and its status), who it was for (the
/// recipient's phone or the merchant), how often it was tried and the last error. <see cref="Created"/> is when it
/// happened, in the courier's time zone. <see cref="GivenUp"/> once every attempt has failed; otherwise it is still being
/// retried.
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
/// The courier's texts and webhooks that failed: given up after every attempt, or still being retried after a failure,
/// newest first. A given-up message can be sent again once the cause is fixed.
/// </summary>
public class FailedMessagesHandler(IAppDbContext db, ITenantContext tenantContext)
{
    public const int Shown = 100;

    public static readonly Error NotFound = Error.NotFound("outbox.notFound", "That message was not found.");

    public async Task<IReadOnlyList<FailedMessage>> ListAsync(CancellationToken cancellationToken = default)
    {
        var tenant = tenantContext.Require();
        var messages = await db.OutboxMessages
            .Where(m => m.Status == OutboxStatus.Failed || (m.Status == OutboxStatus.Pending && m.Attempts > 0))
            .OrderByDescending(m => m.Id)
            .Take(Shown)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var payloads = messages.ToDictionary(m => m.Id, m => JsonSerializer.Deserialize<Ids>(m.Payload)!);
        var parcelIds = payloads.Values.Select(p => p.ParcelId).Distinct().ToList();

        var parcels = await (
            from parcel in db.Parcels
            join merchant in db.Merchants on parcel.MerchantId equals merchant.Id
            where parcelIds.Contains(parcel.Id)
            select new { parcel.Id, parcel.TrackingCode, parcel.RecipientPhone, Merchant = merchant.Name })
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        return [.. messages.Select(Describe)];

        FailedMessage Describe(OutboxMessage message)
        {
            var ids = payloads[message.Id];
            var text = RecipientTexts.Types.Contains(message.Type);
            var (about, to) = parcels.TryGetValue(ids.ParcelId, out var p)
                ? ($"Parcel {p.TrackingCode}: {ids.Status.DisplayName().ToLowerInvariant()}",
                    text ? PhoneNumber.Parse(p.RecipientPhone).Value.Local : p.Merchant)
                : (message.Type, "");

            return new FailedMessage(
                message.Id,
                text,
                about,
                to,
                tenant.Local(message.Created),
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

    /// <summary>The ids every message contract carries.</summary>
    private sealed record Ids(long ParcelId, ParcelStatus Status);
}
