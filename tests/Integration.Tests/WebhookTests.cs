using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Application.Notifications;
using Application.Notifications.SendWebhooks;
using Domain.Merchants;
using Domain.Notifications;
using Domain.Parcels;
using Infrastructure.Persistence;

namespace Integration.Tests;

/// <summary>
/// A parcel's status changes are posted to its merchant's webhook through the outbox, signed with the merchant's secret;
/// the merchant sets the address on its own page. The test database keeps messages from other runs, so the sender runs
/// until the test's own message is handled.
/// </summary>
public class WebhookTests(WebAppFactory factory) : AppTests(factory)
{
    [Fact]
    public async Task A_status_change_is_posted_to_its_merchant_signed_with_the_parcels_own_data()
    {
        WebAppFactory.RequireDatabase();
        var url = NewUrl();
        var shop = await NewMerchantAsync();
        await SetWebhookAsync(shop, url);
        var code = await BookAsync(shop.ApiKey, cod: 900, reference: "WEB-4242");
        Assert.Empty(await MessagesAsync(code));

        await ChangeAsync(code, parcel => parcel.PickUp(Today));
        await ChangeAsync(code, parcel => parcel.ReceiveAt(parcel.PickupHubId, Today));
        var messages = await MessagesAsync(code);
        Assert.Equal(2, messages.Count);
        await SendUntilAsync(messages[^1].Id, m => m.Status == OutboxStatus.Sent, TimeProvider.System);

        var posted = Factory.Webhooks.To(url);
        Assert.Equal([$"msg_{messages[0].Id}", $"msg_{messages[1].Id}"], posted.Select(request => request.Id));
        var secret = await QueryAsync("onedrop", db => db.Merchants.Where(m => m.Id == shop.Id).Select(m => m.WebhookSecret!).SingleAsync(Cancel));
        for (var i = 0; i < posted.Count; i++)
        {
            var request = posted[i];
            Assert.Equal(WebhookSignature.Sign(secret, request.Id, request.Timestamp, request.Body), request.Signature);

            using var body = JsonDocument.Parse(request.Body);
            var root = body.RootElement;
            Assert.Equal("parcel.status_changed", root.GetProperty("type").GetString());
            Assert.Equal(messages[i].Created, root.GetProperty("timestamp").GetDateTime().ToUniversalTime());
            var data = root.GetProperty("data");
            Assert.Equal(code, data.GetProperty("trackingCode").GetString());
            Assert.Equal("WEB-4242", data.GetProperty("merchantReference").GetString());
            Assert.Equal(i == 0 ? "pickedUp" : "atHub", data.GetProperty("status").GetString());
            Assert.Equal(900, data.GetProperty("codAmount").GetDecimal());
        }
    }

    [Fact]
    public async Task A_merchant_without_a_webhook_is_skipped_and_a_failing_one_is_retried_later()
    {
        WebAppFactory.RequireDatabase();
        var plain = await NewMerchantAsync();
        var skipped = await BookAsync(plain.ApiKey);
        await ChangeAsync(skipped, parcel => parcel.PickUp(Today));
        var message = Assert.Single(await MessagesAsync(skipped));
        Assert.Equal(OutboxStatus.Skipped, (await SendUntilAsync(message.Id, m => m.Status != OutboxStatus.Pending, TimeProvider.System)).Status);
        Assert.DoesNotContain(Factory.Webhooks.Posted, request => request.Body.Contains(skipped));

        var url = NewUrl();
        var shop = await NewMerchantAsync();
        await SetWebhookAsync(shop, url);
        var code = await BookAsync(shop.ApiKey);
        await ChangeAsync(code, parcel => parcel.PickUp(Today));
        await ChangeAsync(code, parcel => parcel.ReceiveAt(parcel.PickupHubId, Today));
        var changes = await MessagesAsync(code);
        var (pickedUp, atHub) = (changes[0].Id, changes[1].Id);

        // The server is down: the first change fails and waits a minute; the second is not tried in the same run
        Factory.Webhooks.Down(url);
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var failed = await SendUntilAsync(pickedUp, m => m.Attempts == 1, clock);
        Assert.Equal(OutboxStatus.Pending, failed.Status);
        Assert.Equal(clock.GetUtcNow().UtcDateTime.AddMinutes(1), failed.NextAttemptOn);
        Assert.Equal((OutboxStatus.Pending, 0), await StateAsync(atHub));

        // Back up: the second goes at once, the first once its minute has passed, with the same id
        Factory.Webhooks.Up(url);
        await SendUntilAsync(atHub, m => m.Status == OutboxStatus.Sent, clock);
        clock.Advance(TimeSpan.FromMinutes(1));
        await SendUntilAsync(pickedUp, m => m.Status == OutboxStatus.Sent, clock);
        Assert.Equal([$"msg_{pickedUp}", $"msg_{atHub}", $"msg_{pickedUp}"], Factory.Webhooks.To(url).Select(r => r.Id));
    }

    [Fact]
    public async Task A_merchant_sets_its_webhook_on_its_page_sees_its_secret_and_sends_a_test()
    {
        WebAppFactory.RequireDatabase();
        const string page = "/Merchant/Webhook";
        var shop = await NewMerchantAsync();
        var other = await NewMerchantAsync();
        var merchant = await SignInAsync("onedrop", shop.Email);
        var url = NewUrl();

        Assert.Contains("must start with https://", await merchant.SubmitAsync(page, $"{page}?handler=Save", ("Address", "http://shop.example/hook")));
        Assert.Null(await WebhookAsync(shop));

        await merchant.SubmitAsync(page, $"{page}?handler=Save", ("Address", url));
        var (savedUrl, secret) = (await WebhookAsync(shop))!.Value;
        Assert.Equal(url, savedUrl);
        var shown = await merchant.PageAsync(page);
        Assert.Contains(url, shown);
        Assert.Contains(secret, shown);

        Assert.Contains("answered 200", await merchant.SubmitAsync(page, $"{page}?handler=Test"));
        var test = Assert.Single(Factory.Webhooks.To(url));
        Assert.Equal(WebhookSignature.Sign(secret, test.Id, test.Timestamp, test.Body), test.Signature);
        Assert.Contains("\"type\":\"webhook.test\"", test.Body);

        Factory.Webhooks.Down(url);
        Assert.Contains("Test not delivered: HTTP 500", await merchant.SubmitAsync(page, $"{page}?handler=Test"));
        Factory.Webhooks.Up(url);

        var stranger = await (await SignInAsync("onedrop", other.Email)).PageAsync(page);
        Assert.DoesNotContain(url, stranger);
        Assert.DoesNotContain(secret, stranger);

        await merchant.SubmitAsync(page, $"{page}?handler=Secret");
        Assert.NotEqual(secret, (await WebhookAsync(shop))!.Value.Secret);
        Assert.Contains("no longer sent", await merchant.SubmitAsync(page, $"{page}?handler=Remove"));
        Assert.Null(await WebhookAsync(shop));
        Assert.Contains("Save your webhook address first", await merchant.SubmitAsync(page, $"{page}?handler=Test"));

        Assert.Equal(HttpStatusCode.Redirect, (await Visit("onedrop").GetAsync(page)).StatusCode);
    }

    private static string NewUrl()
    {
        return $"https://shop-{Guid.NewGuid():N}.example/onedrop/webhook";
    }

    private async Task SetWebhookAsync(TestMerchant shop, string url)
    {
        await QueryAsync("onedrop", async db =>
        {
            var merchant = await db.Merchants.SingleAsync(m => m.Id == shop.Id, Cancel);
            Assert.True(merchant.SetWebhook(url).IsSuccess);

            return await db.SaveChangesAsync(Cancel);
        });
    }

    private Task<(string Url, string Secret)?> WebhookAsync(TestMerchant shop)
    {
        return QueryAsync("onedrop", async db =>
        {
            var merchant = await db.Merchants.AsNoTracking().SingleAsync(m => m.Id == shop.Id, Cancel);

            return merchant.WebhookUrl is null ? ((string, string)?)null : (merchant.WebhookUrl, merchant.WebhookSecret!);
        });
    }

    /// <summary>Changes the parcel as hub staff would and saves it with its events.</summary>
    private async Task ChangeAsync(string code, Func<Parcel, Domain.Common.Result> change)
    {
        await QueryAsync("onedrop", async db =>
        {
            var parcel = await db.Parcels.SingleAsync(p => p.TrackingCode == code, Cancel);
            Assert.True(change(parcel).IsSuccess);

            return await db.SaveChangesAsync(Cancel);
        });
    }

    private async Task<List<OutboxMessage>> MessagesAsync(string code)
    {
        var parcelId = await QueryAsync("onedrop", db => db.Parcels.Where(p => p.TrackingCode == code).Select(p => p.Id).SingleAsync(Cancel));
        var messages = await QueryAsync("onedrop", db => db.OutboxMessages
            .AsNoTracking()
            .Where(m => m.Type == nameof(ParcelStatusChangedMessage))
            .Where(m => m.Payload.Contains($"\"ParcelId\":{parcelId},"))
            .OrderBy(m => m.Id)
            .ToListAsync(Cancel));

        return messages;
    }

    private async Task<(OutboxStatus, int)> StateAsync(long id)
    {
        var message = await QueryAsync("onedrop", db => db.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == id, Cancel));

        return (message.Status, message.Attempts);
    }

    /// <summary>Runs the webhook sender until <paramref name="done"/> holds for the message; returns it then.</summary>
    private async Task<OutboxMessage> SendUntilAsync(long messageId, Func<OutboxMessage, bool> done, TimeProvider clock)
    {
        for (var run = 0; run < 40; run++)
        {
            await using (var scope = await ScopeAsync("onedrop"))
            {
                await new SendWebhooksJob(
                        scope.ServiceProvider.GetRequiredService<AppDbContext>(),
                        new MerchantWebhooks(Factory.Webhooks, clock),
                        clock,
                        NullLogger<SendWebhooksJob>.Instance)
                    .RunAsync(Cancel);
            }

            var message = await QueryAsync("onedrop", db => db.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == messageId, Cancel));
            if (done(message))
            {
                return message;
            }
        }

        throw new InvalidOperationException($"Outbox message {messageId} was not handled after 40 runs.");
    }
}
