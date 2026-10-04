using Domain.Merchants;
using Domain.Notifications;
using Domain.Parcels;

namespace Domain.Tests;

/// <summary>A merchant's webhook address and secret, the signature, and the event every parcel status change raises.</summary>
public class WebhookTests
{
    private static readonly DateTime Now = new(2026, 10, 4, 9, 0, 0, DateTimeKind.Utc);

    private static Merchant NewMerchant()
    {
        return Build.Merchant();
    }

    private static ParcelStatus[] Changes(Parcel parcel)
    {
        return [.. parcel.GetDomainEvents().OfType<ParcelStatusChanged>().Select(changed => changed.Status)];
    }

    [Fact]
    public void The_signature_is_the_standard_webhooks_one()
    {
        // The test vector of the Standard Webhooks specification's reference libraries
        var signature = WebhookSignature.Sign(
            "whsec_MfKQ9r8GKYqrTwjUPD8ILPZIo2LaLaSw",
            "msg_p5jXN8AQM9LWM0D4loKWxJek",
            1614265330,
            """{"test": 2432232314}""");

        Assert.Equal("v1,g0hM9SsE+OTPJTGt/tmIKtSyZlE3uFJELVlNIOLJ1OE=", signature);
    }

    [Fact]
    public void Another_secret_id_time_or_body_gives_another_signature()
    {
        var secret = WebhookSignature.NewSecret();
        var signature = WebhookSignature.Sign(secret, "msg_1", 100, "{}");

        Assert.NotEqual(signature, WebhookSignature.Sign(WebhookSignature.NewSecret(), "msg_1", 100, "{}"));
        Assert.NotEqual(signature, WebhookSignature.Sign(secret, "msg_2", 100, "{}"));
        Assert.NotEqual(signature, WebhookSignature.Sign(secret, "msg_1", 101, "{}"));
        Assert.NotEqual(signature, WebhookSignature.Sign(secret, "msg_1", 100, "{ }"));
        Assert.Throws<ArgumentException>(() => WebhookSignature.Sign("not-a-secret", "msg_1", 100, "{}"));
    }

    [Fact]
    public void A_new_secret_is_whsec_and_random_bytes()
    {
        var secret = WebhookSignature.NewSecret();

        Assert.StartsWith(WebhookSignature.SecretPrefix, secret);
        Assert.Equal(WebhookSignature.SecretBytes, Convert.FromBase64String(secret[WebhookSignature.SecretPrefix.Length..]).Length);
        Assert.NotEqual(secret, WebhookSignature.NewSecret());
    }

    [Fact]
    public void Setting_a_webhook_gives_the_shop_a_secret_once_and_keeps_it()
    {
        var merchant = NewMerchant();

        Assert.True(merchant.SetWebhook(" https://myshop.com.bd/hooks/onedrop ").IsSuccess);
        var secret = merchant.WebhookSecret;
        Assert.Equal("https://myshop.com.bd/hooks/onedrop", merchant.WebhookUrl);
        Assert.StartsWith(WebhookSignature.SecretPrefix, secret);

        Assert.True(merchant.SetWebhook("https://shop.example/other").IsSuccess);
        Assert.Equal(secret, merchant.WebhookSecret);

        merchant.RemoveWebhook();
        Assert.Null(merchant.WebhookUrl);
        Assert.Equal(secret, merchant.WebhookSecret);

        merchant.NewWebhookSecret();
        Assert.NotEqual(secret, merchant.WebhookSecret);
    }

    [Theory]
    [InlineData("http://localhost:5080/Dev/Webhooks")]
    [InlineData("http://127.0.0.1:8080/hook")]
    [InlineData("http://shop.localhost/hook")]
    public void Plain_http_is_allowed_only_to_this_machine(string url)
    {
        Assert.True(NewMerchant().SetWebhook(url).IsSuccess);
    }

    [Theory]
    [InlineData(null, "merchant.webhook.url")]
    [InlineData("  ", "merchant.webhook.url")]
    [InlineData("/hooks/onedrop", "merchant.webhook.url")]
    [InlineData("myshop.com.bd/hook", "merchant.webhook.url")]
    [InlineData("ftp://myshop.com.bd/hook", "merchant.webhook.url")]
    [InlineData("https://user:password@myshop.com.bd/hook", "merchant.webhook.url")]
    [InlineData("http://myshop.com.bd/hook", "merchant.webhook.https")]
    [InlineData("http://192.168.1.10/hook", "merchant.webhook.https")]
    public void A_webhook_address_that_is_not_https_or_not_whole_is_refused(string? url, string code)
    {
        var merchant = NewMerchant();

        Assert.Equal(code, merchant.SetWebhook(url).Error?.Code);
        Assert.Null(merchant.WebhookUrl);
        Assert.Null(merchant.WebhookSecret);
    }

    [Fact]
    public void A_webhook_address_too_long_is_refused()
    {
        var url = "https://myshop.com.bd/" + new string('a', Merchant.MaxWebhookUrlLength);

        Assert.Equal("merchant.webhook.url", NewMerchant().SetWebhook(url).Error?.Code);
    }

    [Fact]
    public void Every_status_change_raises_one_event_with_its_status_and_a_new_parcel_none()
    {
        var parcel = Build.Parcel(pickupHub: Build.Mirpur, deliveryHub: Build.Gulshan);
        Assert.Empty(Changes(parcel));

        parcel.PickUp();
        parcel.ReceiveAt(Build.Mirpur);
        parcel.ReceiveAt(Build.Mirpur);
        parcel.DispatchTo(Build.Mirpur, Build.Gulshan);
        parcel.ReceiveAt(Build.Gulshan);
        parcel.AssignTo(riderId: 5, Build.Gulshan);
        parcel.Deliver(parcel.CodAmount, null, Now);

        Assert.Equal(
            [ParcelStatus.PickedUp, ParcelStatus.AtHub, ParcelStatus.InTransit, ParcelStatus.AtHub, ParcelStatus.OutForDelivery, ParcelStatus.Delivered],
            Changes(parcel));
    }

    [Fact]
    public void A_refused_move_raises_nothing()
    {
        var parcel = Build.Parcel();

        Assert.True(parcel.Deliver(0, null, Now).IsFailure);
        Assert.Empty(Changes(parcel));
    }

    [Fact]
    public void A_skipped_message_is_never_due_again()
    {
        var message = OutboxMessage.Create("ParcelStatusChangedMessage", "{}");
        message.MarkFailed("HTTP 500", Now);

        message.MarkSkipped();

        Assert.Equal(OutboxStatus.Skipped, message.Status);
        Assert.Null(message.NextAttemptOn);
        Assert.Null(message.SentOn);
    }
}
