using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Application.Abstractions;
using Application.Notifications;
using Application.Notifications.SendOutbox;
using Application.Notifications.SendWebhooks;
using Domain.Customers;
using Domain.Merchants;
using Domain.Notifications;
using Domain.Orders;
using Infrastructure.MultiTenancy;
using Infrastructure.Persistence;

namespace Integration.Tests;

/// <summary>
/// Task 4.2: an order's status changes are posted to its shop's webhook through the outbox, signed with the shop's
/// secret, and the shop sets the address on its own page. Each test's shop has an address of its own, and the test
/// database keeps messages from other runs, so the sender runs until the test's own message is handled.
/// </summary>
public partial class WebhookTests(WebAppFactory factory)
{
    private const string Password = "OneDrop#2026";
    private const string DhakaArea = "Mirpur 10";

    [Fact]
    public async Task A_status_change_is_posted_to_its_shop_signed_and_saying_nothing_of_the_delivery()
    {
        WebAppFactory.RequireDatabase();
        var url = NewUrl();
        var shop = await NewShopAsync("dhaka", DhakaArea, url);
        var phone = NewPhone();
        var number = await CreateAsync(shop.ApiKey, phone, DhakaArea, reference: "WEB-4242");
        Assert.Empty(await MessagesAsync("dhaka", number));

        // Scanned in at the hub without a pickup scan: collected, then at the hub
        await ChangeAsync("dhaka", number, (order, hubId) => order.ReceiveAtHub(1, hubId, DateTime.UtcNow));
        var messages = await MessagesAsync("dhaka", number);
        Assert.Equal(2, messages.Count);
        await SendUntilAsync("dhaka", messages[^1].Id, m => m.Status == OutboxStatus.Sent, TimeProvider.System);

        var posted = factory.Webhooks.To(url);
        Assert.Equal([$"msg_{messages[0].Id}", $"msg_{messages[1].Id}"], posted.Select(request => request.Id));
        var secret = await SecretAsync("dhaka", shop.Name);
        for (var i = 0; i < posted.Count; i++)
        {
            var request = posted[i];
            Assert.Equal(WebhookSignature.Sign(secret, request.Id, request.Timestamp, request.Body), request.Signature);
            Assert.InRange(
                DateTimeOffset.FromUnixTimeSeconds(request.Timestamp),
                DateTimeOffset.UtcNow.AddMinutes(-1),
                DateTimeOffset.UtcNow.AddMinutes(1));
            Assert.DoesNotContain("DG-", request.Body);
            Assert.DoesNotContain(PhoneNumber.Parse(phone).Value.Value[4..], request.Body);

            using var body = JsonDocument.Parse(request.Body);
            var root = body.RootElement;
            Assert.Equal("order.status_changed", root.GetProperty("type").GetString());
            Assert.Equal(messages[i].Created, root.GetProperty("timestamp").GetDateTime().ToUniversalTime());
            var data = root.GetProperty("data");
            Assert.Equal(number, data.GetProperty("number").GetString());
            Assert.Equal("WEB-4242", data.GetProperty("externalReference").GetString());
            Assert.Equal(i == 0 ? "pickedUp" : "atHub", data.GetProperty("status").GetString());
            Assert.Equal(["type", "timestamp", "data"], root.EnumerateObject().Select(p => p.Name));
            Assert.Equal(["number", "externalReference", "status"], data.EnumerateObject().Select(p => p.Name));
        }

        Assert.All(await MessagesAsync("dhaka", number), m => Assert.Equal((OutboxStatus.Sent, 1), (m.Status, m.Attempts)));
    }

    [Fact]
    public async Task A_shop_without_a_webhook_has_its_changes_skipped()
    {
        WebAppFactory.RequireDatabase();
        var shop = await NewShopAsync("dhaka", DhakaArea, url: null);
        var number = await CreateAsync(shop.ApiKey, NewPhone(), DhakaArea);

        await ChangeAsync("dhaka", number, (order, _) => order.Collect());
        var message = (await MessagesAsync("dhaka", number)).Single();
        var handled = await SendUntilAsync("dhaka", message.Id, m => m.Status != OutboxStatus.Pending, TimeProvider.System);

        Assert.Equal(OutboxStatus.Skipped, handled.Status);
        Assert.Equal(0, handled.Attempts);
        Assert.DoesNotContain(factory.Webhooks.Posted, request => request.Body.Contains(number));
    }

    [Fact]
    public async Task A_shop_whose_server_fails_is_tried_again_later_and_not_called_again_in_the_same_run()
    {
        WebAppFactory.RequireDatabase();
        var url = NewUrl();
        var shop = await NewShopAsync("dhaka", DhakaArea, url);
        var number = await CreateAsync(shop.ApiKey, NewPhone(), DhakaArea);
        await ChangeAsync("dhaka", number, (order, hubId) => order.ReceiveAtHub(1, hubId, DateTime.UtcNow));
        var messages = await MessagesAsync("dhaka", number);
        var (collected, atHub) = (messages[0].Id, messages[1].Id);
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow.AddSeconds(1));
        factory.Webhooks.Down(url);

        var failed = await SendUntilAsync("dhaka", collected, m => m.Attempts == 1, clock);

        Assert.Equal(OutboxStatus.Pending, failed.Status);
        Assert.Equal("HTTP 500", failed.LastError);
        Assert.Equal(clock.GetUtcNow().UtcDateTime.AddMinutes(1), failed.NextAttemptOn);
        Assert.Equal(0, (await FindAsync("dhaka", atHub)).Attempts);
        Assert.Single(factory.Webhooks.To(url));

        // The server is back: the later change goes on the next run, the failed one once its wait is over
        factory.Webhooks.Up(url);
        await SendUntilAsync("dhaka", atHub, m => m.Status == OutboxStatus.Sent, clock);
        Assert.Equal(1, (await FindAsync("dhaka", collected)).Attempts);
        clock.Advance(TimeSpan.FromMinutes(1));
        var sent = await SendUntilAsync("dhaka", collected, m => m.Status == OutboxStatus.Sent, clock);

        Assert.Equal(2, sent.Attempts);
        Assert.Equal([$"msg_{collected}", $"msg_{atHub}", $"msg_{collected}"], factory.Webhooks.To(url).Select(r => r.Id));
    }

    [Fact]
    public async Task Each_operator_sends_its_own_and_the_texts_sender_leaves_webhooks_alone()
    {
        WebAppFactory.RequireDatabase();
        var url = NewUrl();
        string area;
        await using (var scope = await ScopeAsync("chattogram"))
        {
            area = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Areas
                .OrderBy(a => a.Id)
                .Select(a => a.Name)
                .FirstAsync(Cancel);
        }

        var shop = await NewShopAsync("chattogram", area, url);
        var number = await CreateAsync(shop.ApiKey, NewPhone(), area);
        await ChangeAsync("chattogram", number, (order, _) => order.Collect());
        var message = (await MessagesAsync("chattogram", number)).Single();

        await DrainTextsAsync("chattogram");
        Assert.Equal((OutboxStatus.Pending, 0), await StateAsync("chattogram", message.Id));

        await SendOnceAsync("dhaka", TimeProvider.System);
        Assert.Equal((OutboxStatus.Pending, 0), await StateAsync("chattogram", message.Id));
        Assert.Empty(factory.Webhooks.To(url));

        await SendUntilAsync("chattogram", message.Id, m => m.Status == OutboxStatus.Sent, TimeProvider.System);
        var request = Assert.Single(factory.Webhooks.To(url));
        Assert.Contains("\"status\":\"pickedUp\"", request.Body);
        Assert.Equal(
            WebhookSignature.Sign(await SecretAsync("chattogram", shop.Name), request.Id, request.Timestamp, request.Body),
            request.Signature);
    }

    [Fact]
    public async Task A_shop_sets_its_webhook_on_its_page_sees_its_secret_and_sends_a_test()
    {
        WebAppFactory.RequireDatabase();
        const string page = "/Merchant/Webhook";
        var gadget = await SignInAsync("gadget@dhaka.onedrop.test");
        var fashion = await SignInAsync("fashion@dhaka.onedrop.test");
        var url = NewUrl();

        Assert.Contains("must start with https://", await PostPageAsync(gadget, page, "Save", "http://gadget.example/hook"));
        Assert.Null(await WebhookAsync("Gadget BD"));

        await PostPageAsync(gadget, page, "Save", url);
        var (savedUrl, secret) = (await WebhookAsync("Gadget BD"))!.Value;
        Assert.Equal(url, savedUrl);
        var shown = WebUtility.HtmlDecode(await gadget.GetStringAsync(page, Cancel));
        Assert.Contains(url, shown);
        Assert.Contains(secret, shown);

        Assert.Contains("answered 200", await PostPageAsync(gadget, page, "Test"));
        var test = Assert.Single(factory.Webhooks.To(url));
        Assert.Equal(WebhookSignature.Sign(secret, test.Id, test.Timestamp, test.Body), test.Signature);
        Assert.Contains("\"type\":\"webhook.test\"", test.Body);
        Assert.Matches("\"timestamp\":\"[0-9-]+T[0-9]{2}:[0-9]{2}:[0-9]{2}Z\"", test.Body);

        factory.Webhooks.Down(url);
        Assert.Contains("Test not delivered: HTTP 500", await PostPageAsync(gadget, page, "Test"));
        factory.Webhooks.Up(url);

        var other = WebUtility.HtmlDecode(await fashion.GetStringAsync(page, Cancel));
        Assert.DoesNotContain(url, other);
        Assert.DoesNotContain(secret, other);

        await PostPageAsync(gadget, page, "Secret");
        Assert.NotEqual(secret, (await WebhookAsync("Gadget BD"))!.Value.Secret);

        Assert.Contains("no longer sent", await PostPageAsync(gadget, page, "Remove"));
        Assert.Null(await WebhookAsync("Gadget BD"));
        Assert.Contains("Save your webhook address first", await PostPageAsync(gadget, page, "Test"));

        var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://dhaka.localhost"),
            AllowAutoRedirect = false
        });
        Assert.Equal(HttpStatusCode.Redirect, (await anonymous.GetAsync(page, Cancel)).StatusCode);
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static string NewUrl()
    {
        return $"https://shop-{Guid.NewGuid():N}.example/onedrop/webhook";
    }

    /// <summary>Runs the webhook sender until <paramref name="done"/> holds for the message; returns it then.</summary>
    private async Task<OutboxMessage> SendUntilAsync(
        string slug,
        long messageId,
        Func<OutboxMessage, bool> done,
        TimeProvider clock)
    {
        for (var run = 0; run < 40; run++)
        {
            await SendOnceAsync(slug, clock);
            var message = await FindAsync(slug, messageId);
            if (done(message))
            {
                return message;
            }
        }

        throw new InvalidOperationException($"Outbox message {messageId} was not handled after 40 runs.");
    }

    private async Task SendOnceAsync(string slug, TimeProvider clock)
    {
        await using var scope = await ScopeAsync(slug);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await new SendWebhooksJob(
                db,
                new MerchantWebhooks(factory.Webhooks, clock),
                clock,
                NullLogger<SendWebhooksJob>.Instance)
            .RunAsync(Cancel);
    }

    /// <summary>Runs the texts sender until no text is due, with a gateway that sends nothing anywhere.</summary>
    private async Task DrainTextsAsync(string slug)
    {
        for (var run = 0; run < 40; run++)
        {
            await using var scope = await ScopeAsync(slug);
            var services = scope.ServiceProvider;
            var db = services.GetRequiredService<AppDbContext>();
            var texts = ActivatorUtilities.CreateInstance<CustomerTexts>(services, (ISmsSender)new NoSms());
            await new SendOutboxJob(db, texts, TimeProvider.System, NullLogger<SendOutboxJob>.Instance).RunAsync(Cancel);

            var now = DateTime.UtcNow;
            if (!await db.OutboxMessages.AnyAsync(
                m => m.Status == OutboxStatus.Pending && (m.NextAttemptOn == null || m.NextAttemptOn <= now) &&
                    CustomerTexts.Types.Contains(m.Type),
                Cancel))
            {
                return;
            }
        }
    }

    /// <summary>The order's status change messages, oldest first.</summary>
    private async Task<List<OutboxMessage>> MessagesAsync(string slug, string number)
    {
        await using var scope = await ScopeAsync(slug);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var orderId = await db.Orders.Where(o => o.Number == number).Select(o => o.Id).SingleAsync(Cancel);
        var start = $"{{\"OrderId\":{orderId.ToString(CultureInfo.InvariantCulture)},";

        return await db.OutboxMessages
            .AsNoTracking()
            .Where(m => m.Type == nameof(OrderStatusChangedMessage) && m.Payload.StartsWith(start))
            .OrderBy(m => m.Id)
            .ToListAsync(Cancel);
    }

    private async Task<OutboxMessage> FindAsync(string slug, long id)
    {
        await using var scope = await ScopeAsync(slug);

        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().OutboxMessages
            .AsNoTracking()
            .SingleAsync(m => m.Id == id, Cancel);
    }

    private async Task<(OutboxStatus, int)> StateAsync(string slug, long id)
    {
        var message = await FindAsync(slug, id);

        return (message.Status, message.Attempts);
    }

    /// <summary>Changes the order as hub staff would, at the operator's first hub, and saves it with its events.</summary>
    private async Task ChangeAsync(string slug, string number, Action<Order, long> change)
    {
        await using var scope = await ScopeAsync(slug);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var order = await db.Orders.Include(o => o.Packages).SingleAsync(o => o.Number == number, Cancel);
        var hubId = await db.Hubs.OrderBy(h => h.Id).Select(h => h.Id).FirstAsync(Cancel);

        change(order, hubId);
        await db.SaveChangesAsync(Cancel);
    }

    private async Task<string> SecretAsync(string slug, string shop)
    {
        await using var scope = await ScopeAsync(slug);

        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Merchants
            .Where(m => m.Name == shop)
            .Select(m => m.WebhookSecret!)
            .SingleAsync(Cancel);
    }

    private async Task<(string Url, string Secret)?> WebhookAsync(string shop)
    {
        await using var scope = await ScopeAsync("dhaka");
        var merchant = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Merchants
            .AsNoTracking()
            .SingleAsync(m => m.Name == shop, Cancel);

        return merchant.WebhookUrl is null ? null : (merchant.WebhookUrl, merchant.WebhookSecret!);
    }

    /// <summary>A shop of this test's own, its default pickup point in <paramref name="area"/>, with a webhook or none.</summary>
    private async Task<(string Name, string ApiKey)> NewShopAsync(string slug, string area, string? url)
    {
        await using var scope = await ScopeAsync(slug);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var name = $"Hook Shop {Guid.NewGuid():N}"[..19];
        var pickupArea = await db.Areas.SingleAsync(a => a.Name == area, Cancel);
        var merchant = new Merchant(name, pickupArea.ZoneId, "01711999999", null);
        if (url is not null)
        {
            Assert.True(merchant.SetWebhook(url).IsSuccess);
        }

        db.Merchants.Add(merchant);
        await db.SaveChangesAsync(Cancel);

        var (key, plaintext) = MerchantApiKey.Issue(merchant.Id, "Webhook test");
        db.PickupPoints.Add(new PickupPoint(merchant.Id, pickupArea.Id, "Shop", $"{name}, {area}", "01711999999", isDefault: true));
        db.MerchantApiKeys.Add(key);
        await db.SaveChangesAsync(Cancel);

        return (name, plaintext);
    }

    /// <summary>A product paid online, so the order waits for nothing from the customer.</summary>
    private async Task<string> CreateAsync(string apiKey, string phone, string area, string? reference = null)
    {
        var order = new
        {
            ExternalReference = reference,
            Customer = new { Name = "Webhook Customer", Phone = phone },
            Address = new { Area = area, Line1 = "House 3, Road 9" },
            Packages = new[] { new { Description = "Parcel", WeightGrams = 400 } },
            CodAmount = 0
        };
        var response = await factory.ClientFor(apiKey).PostAsJsonAsync("/api/v1/orders", order, Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<Created>(Cancel))!.Number;
    }

    /// <summary>Posts one of the page's forms and returns the page it lands on.</summary>
    private async Task<string> PostPageAsync(HttpClient client, string page, string handler, string? address = null)
    {
        var token = Token().Match(await client.GetStringAsync(page, Cancel)).Groups[1].Value;
        var fields = new Dictionary<string, string> { ["__RequestVerificationToken"] = token };
        if (address is not null)
        {
            fields["Address"] = address;
        }

        var response = await client.PostAsync($"{page}?handler={handler}", new FormUrlEncodedContent(fields), Cancel);
        if (response.StatusCode == HttpStatusCode.Redirect)
        {
            return await client.GetStringAsync(response.Headers.Location!.OriginalString, Cancel);
        }

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadAsStringAsync(Cancel);
    }

    /// <summary>Signs a demo shop in on Dhaka's subdomain. Keeps the sign-in cookie.</summary>
    private async Task<HttpClient> SignInAsync(string email)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://dhaka.localhost"),
            AllowAutoRedirect = false
        });
        var token = Token().Match(await client.GetStringAsync("/Account/Login", Cancel)).Groups[1].Value;
        var fields = new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = Password,
            ["__RequestVerificationToken"] = token
        };

        var signedIn = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(fields), Cancel);
        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);

        return client;
    }

    private async Task<AsyncServiceScope> ScopeAsync(string slug)
    {
        var scope = factory.Services.CreateAsyncScope();
        var tenant = await factory.Services.GetRequiredService<ITenantCatalog>().FindBySlugAsync(slug, Cancel);
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(tenant!);

        return scope;
    }

    private static string NewPhone()
    {
        return "017" + Random.Shared.Next(0, 100_000_000).ToString("D8");
    }

    [GeneratedRegex("""name="__RequestVerificationToken" type="hidden" value="([^"]+)""")]
    private static partial Regex Token();

    private sealed class NoSms : ISmsSender
    {
        public Task SendAsync(PhoneNumber to, string senderName, string text, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed record Created(string Number);
}
