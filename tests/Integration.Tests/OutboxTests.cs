using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Application.Abstractions;
using Application.Notifications;
using Application.Notifications.SendOutbox;
using Domain.Common;
using Domain.Notifications;
using Domain.Parcels;
using Infrastructure.Persistence;

namespace Integration.Tests;

/// <summary>
/// The recipient is texted when the parcel goes out and when it is delivered; the texts are written in the change's own
/// save and sent later, with retries. Each test records texts with a gateway of its own and runs the sender until its
/// own message is handled (the database keeps other runs' messages).
/// </summary>
[Collection("Texts")]
public class OutboxTests(WebAppFactory factory) : AppTests(factory)
{
    [Fact]
    public async Task Booking_texts_the_recipient_a_tracking_link()
    {
        WebAppFactory.RequireDatabase();
        var shop = await NewMerchantAsync();
        var phone = NewPhone();
        var code = await BookAsync(shop.ApiKey, area: "Pallabi", phone: phone);

        var sms = new RecordingSms();
        await SendUntilAsync(Assert.Single(await TextsAsync(code)).Id, m => m.Status == OutboxStatus.Sent, sms, TimeProvider.System);

        var text = Assert.Single(sms.To(phone));
        Assert.Equal("OneDrop", text.Sender);
        Assert.StartsWith($"Your parcel {code} from {shop.Name} has been booked with OneDrop Courier.", text.Text);
        Assert.Contains($"/Track?code={code}", text.Text);
    }

    [Fact]
    public async Task Going_out_and_being_delivered_text_the_recipient_from_the_couriers_sender_name()
    {
        WebAppFactory.RequireDatabase();
        var shop = await NewMerchantAsync();
        var phone = NewPhone();
        var code = await BookAsync(shop.ApiKey, area: "Pallabi", cod: 1500, phone: phone);

        // Send the booking text while the parcel is still pending; this test is about the two that follow it
        var sms = new RecordingSms();
        await SendUntilAsync(Assert.Single(await TextsAsync(code)).Id, m => m.Status == OutboxStatus.Sent, sms, TimeProvider.System);

        await ChangeAsync(code, (parcel, rider) => parcel.ReceiveAt(parcel.DeliveryHubId));
        await ChangeAsync(code, (parcel, rider) => parcel.AssignTo(rider, parcel.DeliveryHubId));
        var afterAssign = await TextsAsync(code);
        Assert.Equal(2, afterAssign.Count);
        await SendUntilAsync(afterAssign[^1].Id, m => m.Status == OutboxStatus.Sent, sms, TimeProvider.System);
        await ChangeAsync(code, (parcel, _) => parcel.Deliver(1500, null, DateTime.UtcNow));
        var messages = await TextsAsync(code);
        Assert.Equal(3, messages.Count);
        await SendUntilAsync(messages[^1].Id, m => m.Status == OutboxStatus.Sent, sms, TimeProvider.System);

        var texts = sms.To(phone);
        Assert.Equal(3, texts.Count);
        Assert.All(texts, text => Assert.Equal("OneDrop", text.Sender));
        Assert.StartsWith($"Your parcel {code} from {shop.Name} has been booked with OneDrop Courier.", texts[0].Text);
        Assert.StartsWith($"Your parcel {code} from {shop.Name} is out for delivery today with ", texts[1].Text);
        Assert.Contains("Please keep ৳1,500 ready.", texts[1].Text);
        Assert.Contains($"/Track?code={code}", texts[1].Text);
        Assert.StartsWith($"Your parcel {code} from {shop.Name} has been delivered, ৳1,500 paid.", texts[2].Text);
    }

    [Fact]
    public async Task A_failed_text_waits_longer_each_time_and_is_given_up_after_the_last_attempt()
    {
        WebAppFactory.RequireDatabase();
        var shop = await NewMerchantAsync();
        var code = await BookAsync(shop.ApiKey, area: "Pallabi");
        await ChangeAsync(code, (parcel, _) => parcel.ReceiveAt(parcel.DeliveryHubId));
        await ChangeAsync(code, (parcel, rider) => parcel.AssignTo(rider, parcel.DeliveryHubId));
        var id = (await TextsAsync(code))[^1].Id; // the out-for-delivery text; the booking text precedes it
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var down = new RecordingSms { Down = true };

        foreach (var wait in new[] { 1, 2, 4, 8 })
        {
            var failed = await SendUntilAsync(id, m => m.Attempts > 0 && m.NextAttemptOn > clock.GetUtcNow().UtcDateTime, down, clock);
            Assert.Equal(clock.GetUtcNow().UtcDateTime.AddMinutes(wait), failed.NextAttemptOn);
            clock.Advance(TimeSpan.FromSeconds(wait * 60 - 1));
            await SendOnceAsync(down, clock);
            Assert.Equal(failed.Attempts, (await MessageAsync(id)).Attempts);
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        var givenUp = await SendUntilAsync(id, m => m.Status != OutboxStatus.Pending, down, clock);
        Assert.Equal((OutboxStatus.Failed, OutboxMessage.MaxAttempts), (givenUp.Status, givenUp.Attempts));
    }

    /// <summary>Changes the parcel with a rider of its delivery hub at hand, and saves it with its events.</summary>
    private async Task ChangeAsync(string code, Func<Parcel, long, Result> change)
    {
        await QueryAsync("onedrop", async db =>
        {
            var parcel = await db.Parcels.SingleAsync(p => p.TrackingCode == code, Cancel);
            var rider = await db.Riders.Where(r => r.HubId == parcel.DeliveryHubId).Select(r => r.Id).FirstAsync(Cancel);
            Assert.True(change(parcel, rider).IsSuccess);

            return await db.SaveChangesAsync(Cancel);
        });
    }

    private async Task<List<OutboxMessage>> TextsAsync(string code)
    {
        var parcelId = await QueryAsync("onedrop", db => db.Parcels.Where(p => p.TrackingCode == code).Select(p => p.Id).SingleAsync(Cancel));

        return await QueryAsync("onedrop", db => db.OutboxMessages
            .AsNoTracking()
            .Where(m => m.Type == nameof(RecipientTextMessage) && m.Payload.Contains($"\"ParcelId\":{parcelId},"))
            .OrderBy(m => m.Id)
            .ToListAsync(Cancel));
    }

    private Task<OutboxMessage> MessageAsync(long id)
    {
        return QueryAsync("onedrop", db => db.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == id, Cancel));
    }

    private async Task<OutboxMessage> SendUntilAsync(long id, Func<OutboxMessage, bool> done, RecordingSms sms, TimeProvider clock)
    {
        for (var run = 0; run < 40; run++)
        {
            await SendOnceAsync(sms, clock);
            var message = await MessageAsync(id);
            if (done(message))
            {
                return message;
            }
        }

        throw new InvalidOperationException($"Outbox message {id} was not handled after 40 runs.");
    }

    private async Task SendOnceAsync(RecordingSms sms, TimeProvider clock)
    {
        await using var scope = await ScopeAsync("onedrop");
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<AppDbContext>();
        var texts = new RecipientTexts(db, services.GetRequiredService<ITenantContext>(), sms, services.GetRequiredService<ITrackingLinks>());

        await new SendOutboxJob(db, texts, clock, NullLogger<SendOutboxJob>.Instance).RunAsync(Cancel);
    }

    private sealed class RecordingSms : ISmsSender
    {
        private readonly ConcurrentQueue<(string To, string Sender, string Text)> sent = new();

        public bool Down { get; init; }

        public IReadOnlyList<(string To, string Sender, string Text)> To(string phone)
        {
            var e164 = PhoneNumber.Parse(phone).Value.Value;

            return [.. sent.Where(text => text.To == e164)];
        }

        public Task SendAsync(PhoneNumber to, string senderName, string text, CancellationToken cancellationToken = default)
        {
            if (Down)
            {
                throw new HttpRequestException("Gateway down");
            }

            sent.Enqueue((to.Value, senderName, text));

            return Task.CompletedTask;
        }
    }
}
