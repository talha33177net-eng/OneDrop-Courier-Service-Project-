using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Application.Notifications;
using Domain.Notifications;
using Domain.Parcels;
using Infrastructure.Persistence;

namespace Integration.Tests;

/// <summary>
/// The courier's admin sees the texts and webhooks that have not gone out and sends a given-up one again; another courier
/// sees none of them. The test host runs no sender, so a message stays where the test puts it.
/// </summary>
[Collection("Texts")]
public class FailedMessagesTests(WebAppFactory factory) : AppTests(factory)
{
    private const string Page = "/Admin/Messages";

    [Fact]
    public async Task An_admin_sees_what_failed_and_sends_a_given_up_text_again()
    {
        WebAppFactory.RequireDatabase();
        var shop = await NewMerchantAsync();
        var phone = NewPhone();
        var givenUp = await OutForDeliveryAsync(shop, phone);
        var retrying = await OutForDeliveryAsync(shop, phone);
        var givenUpId = await FailAsync(givenUp, OutboxMessage.MaxAttempts);
        await FailAsync(retrying, 1);
        var admin = await SignInAsync("onedrop", "admin@onedrop.test");
        var rival = await SignInAsync("rival", "admin@rival.test");

        // Both listed with what they were about and for whom; only the given-up one can be sent again
        var page = await admin.PageAsync(Page);
        Assert.Contains($"Parcel {givenUp}: out for delivery", page);
        Assert.Contains($"Parcel {retrying}: out for delivery", page);
        Assert.Contains(phone, page);
        Assert.Contains("Gateway down", page);
        Assert.Contains("Given up", page);
        Assert.Contains("Retrying", page);
        Assert.Contains($"name=\"id\" value=\"{givenUpId}\"", page);

        // Another courier sees none of it and cannot send it
        Assert.DoesNotContain(givenUp, await rival.PageAsync(Page));
        Assert.Equal(HttpStatusCode.NotFound, (await rival.PostFormAsync(Page, $"{Page}?handler=SendAgain", ("id", $"{givenUpId}"))).StatusCode);
        Assert.Equal(OutboxStatus.Failed, (await MessageAsync(givenUpId)).Status);

        // Sent again: due at once with fresh attempts, and no longer listed
        Assert.Equal(HttpStatusCode.Redirect, (await admin.PostFormAsync(Page, $"{Page}?handler=SendAgain", ("id", $"{givenUpId}"))).StatusCode);
        var message = await MessageAsync(givenUpId);
        Assert.Equal((OutboxStatus.Pending, 0, null), (message.Status, message.Attempts, message.NextAttemptOn));
        Assert.DoesNotContain($"Parcel {givenUp}:", await admin.PageAsync(Page));

        // Hub staff and merchants do not get the page
        Assert.NotEqual(HttpStatusCode.OK, (await (await SignInAsync("onedrop", "hub@onedrop.test")).GetAsync(Page)).StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, (await (await SignInAsync("onedrop", shop.Email)).GetAsync(Page)).StatusCode);
    }

    /// <summary>Books a parcel and hands it to a rider, which queues the recipient's "out for delivery" text.</summary>
    private async Task<string> OutForDeliveryAsync(TestMerchant shop, string phone)
    {
        var code = await BookAsync(shop.ApiKey, area: "Pallabi", phone: phone);
        await using var scope = await ScopeAsync("onedrop");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var parcel = await db.Parcels.SingleAsync(p => p.TrackingCode == code, Cancel);
        var rider = await db.Riders.Where(r => r.HubId == parcel.DeliveryHubId).Select(r => r.Id).FirstAsync(Cancel);
        Assert.True(parcel.ReceiveAt(parcel.DeliveryHubId, Today).IsSuccess);
        Assert.True(parcel.AssignTo(rider, parcel.DeliveryHubId).IsSuccess);
        await db.SaveChangesAsync(Cancel);

        return code;
    }

    /// <summary>Fails the parcel's "out for delivery" text as often as asked, as the sender would; returns its id.</summary>
    private async Task<long> FailAsync(string code, int times)
    {
        await using var scope = await ScopeAsync("onedrop");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var parcelId = await db.Parcels.Where(p => p.TrackingCode == code).Select(p => p.Id).SingleAsync(Cancel);
        var payload = JsonSerializer.Serialize(new RecipientTextMessage(parcelId, ParcelStatus.OutForDelivery));
        var message = await db.OutboxMessages.SingleAsync(m => m.Type == nameof(RecipientTextMessage) && m.Payload == payload, Cancel);
        for (var attempt = 0; attempt < times; attempt++)
        {
            message.MarkFailed("Gateway down", DateTime.UtcNow);
        }

        await db.SaveChangesAsync(Cancel);

        return message.Id;
    }

    private Task<OutboxMessage> MessageAsync(long id)
    {
        return QueryAsync("onedrop", db => db.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == id, Cancel));
    }
}
