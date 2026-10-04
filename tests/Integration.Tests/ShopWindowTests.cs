using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Application.Abstractions;
using Application.Grouping.CustomerDeliveries;
using Application.Notifications;
using Application.Notifications.SendOutbox;
using Domain.Customers;
using Domain.Merchants;
using Infrastructure.MultiTenancy;
using Infrastructure.Persistence;
using Infrastructure.Sms;

namespace Integration.Tests;

/// <summary>
/// Task 4.8: shops that list themselves are shown to customers whose delivery is still open to other shops, in the
/// "joined" text (a link to the operator's shops page) and on "My deliveries" (without the shops already in that
/// delivery). Shops of the tests' own, collected from Mirpur 1, and every customer new.
/// </summary>
public partial class ShopWindowTests(WebAppFactory factory)
{
    private const string Password = "OneDrop#2026";

    [Fact]
    public async Task Listed_shops_are_offered_on_open_deliveries_without_the_ones_already_in_them()
    {
        WebAppFactory.RequireDatabase();
        var dhaka = await TenantAsync("dhaka");
        var inIt = await NewShopAsync("dhaka", list: true);
        var other = await NewShopAsync("dhaka", list: true);
        var unlisted = await NewShopAsync("dhaka", list: false);
        var elsewhere = await NewShopAsync("chattogram", list: true);
        try
        {
            var phone = NewPhone();
            var placed = await CreateAsync(inIt.ApiKey, phone, "combine");
            var fast = await CreateAsync(unlisted.ApiKey, phone, "fast");

            // The open delivery offers every listed shop of the operator but the one in it
            var deliveries = await DeliveriesAsync(await CustomerIdAsync(phone));
            var open = deliveries.OnTheWay.Single(d => d.Orders.Any(o => o.Number == placed));
            var shops = open.MoreShops!.Select(shop => shop.Name).ToList();
            Assert.Contains(other.Name, shops);
            Assert.DoesNotContain(inIt.Name, shops);
            Assert.DoesNotContain(unlisted.Name, shops);
            Assert.DoesNotContain(elsewhere.Name, shops);
            var offered = open.MoreShops!.Single(shop => shop.Name == other.Name);
            Assert.Equal((Url(other.Name), "Test kantha"), (offered.Url, offered.About));

            // A delivery leaving tomorrow is not open to shops: no window
            Assert.Null(deliveries.OnTheWay.Single(d => d.Orders.Any(o => o.Number == fast)).MoreShops);

            // The "joined" text links the window with the extra-shop fee; the next-day one does not
            var text = (await TextAsync(placed)).Text;
            Assert.Contains($" Add from any OneDrop shop for +৳{dhaka.ExtraShopFee:N0}: ", text);
            Assert.EndsWith("/Shops", text);
            Assert.DoesNotContain("Add from any OneDrop shop", (await TextAsync(fast)).Text);

            // The shops page is the same for everyone: listed shops of this operator only, with their links
            var page = WebUtility.HtmlDecode(await Client("dhaka").GetStringAsync("/Shops", Cancel));
            Assert.Contains(other.Name, page);
            Assert.Contains(inIt.Name, page);
            Assert.Contains($"href=\"{Url(other.Name)}\"", page);
            Assert.Contains("Test kantha", page);
            Assert.DoesNotContain(unlisted.Name, page);
            Assert.DoesNotContain(elsewhere.Name, page);
            var ctgPage = await Client("chattogram").GetStringAsync("/Shops", Cancel);
            Assert.Contains(elsewhere.Name, ctgPage);
            Assert.DoesNotContain(other.Name, ctgPage);
            Assert.Equal(HttpStatusCode.NotFound, (await Client(null).GetAsync("/Shops", Cancel)).StatusCode);

            // A shop that leaves is offered no more
            await LeaveAsync("dhaka", other.Name);
            var after = (await DeliveriesAsync(await CustomerIdAsync(phone))).OnTheWay.Single(d => d.Number == open.Number);
            Assert.DoesNotContain(other.Name, after.MoreShops!.Select(shop => shop.Name));
        }
        finally
        {
            await LeaveAsync("dhaka", inIt.Name, other.Name);
            await LeaveAsync("chattogram", elsewhere.Name);
        }
    }

    [Fact]
    public async Task With_no_other_listed_shop_the_text_does_not_offer_the_window()
    {
        WebAppFactory.RequireDatabase();

        // Only these tests list Chattogram shops, and each leaves the window again (this clears an interrupted run's)
        await LeaveAsync("chattogram", [.. await OtherListedAsync("chattogram", "")]);
        var only = await NewShopAsync("chattogram", list: true);
        try
        {
            Assert.Empty(await OtherListedAsync("chattogram", only.Name));
            var placed = await CreateAsync(only.ApiKey, NewPhone(), "combine", "Agrabad");

            var text = (await TextAsync(placed, "chattogram")).Text;

            Assert.Contains("Orders from other shops can join it until the end of", text);
            Assert.DoesNotContain("Add from any OneDrop shop", text);
        }
        finally
        {
            await LeaveAsync("chattogram", only.Name);
        }
    }

    [Fact]
    public async Task A_shop_lists_itself_on_its_page_and_another_shop_cannot_see_or_change_it()
    {
        WebAppFactory.RequireDatabase();
        const string page = "/Merchant/Window";
        var beauty = await SignInAsync("beauty@dhaka.onedrop.test");
        var gadget = await SignInAsync("gadget@dhaka.onedrop.test");
        var url = $"https://beauty-{Guid.NewGuid():N}.example/";
        try
        {
            Assert.Contains(
                "Enter the full address of your shop",
                await PostPageAsync(beauty, page, "List", ("Address", "beauty.example"), ("About", "Lipsticks")));
            Assert.Null(await ListingAsync("Beauty Shop"));

            Assert.Contains(
                "Customers with a delivery still open now see your shop",
                await PostPageAsync(beauty, page, "List", ("Address", url), ("About", "Lipsticks and creams")));
            Assert.Equal((url, "Lipsticks and creams"), await ListingAsync("Beauty Shop"));
            Assert.Contains(url, WebUtility.HtmlDecode(await beauty.GetStringAsync(page, Cancel)));
            Assert.Contains(url, await Client("dhaka").GetStringAsync("/Shops", Cancel));

            // Another shop sees only its own settings, and its forms change only its own row
            Assert.DoesNotContain(url, WebUtility.HtmlDecode(await gadget.GetStringAsync(page, Cancel)));
            await PostPageAsync(gadget, page, "Leave");
            Assert.Equal((url, "Lipsticks and creams"), await ListingAsync("Beauty Shop"));

            Assert.Contains("no longer in the shopping window", await PostPageAsync(beauty, page, "Leave"));
            Assert.Null(await ListingAsync("Beauty Shop"));
            Assert.DoesNotContain(url, await Client("dhaka").GetStringAsync("/Shops", Cancel));

            var anonymous = await Client("dhaka").GetAsync(page, Cancel);
            Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        }
        finally
        {
            await LeaveAsync("dhaka", "Beauty Shop");
        }
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static string Url(string shop)
    {
        return $"https://{shop.Replace(' ', '-').ToLowerInvariant()}.example/";
    }

    private async Task<CustomerDeliveries> DeliveriesAsync(long customerId)
    {
        await using var scope = await ScopeAsync("dhaka");

        return await scope.ServiceProvider.GetRequiredService<CustomerDeliveriesHandler>().HandleAsync(customerId, Cancel);
    }

    private async Task<long> CustomerIdAsync(string phone)
    {
        await using var scope = await ScopeAsync("dhaka");
        var e164 = PhoneNumber.Parse(phone).Value.Value;

        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Customers
            .Where(c => c.Phone == e164)
            .Select(c => c.Id)
            .SingleAsync(Cancel);
    }

    private async Task<(string Url, string? About)?> ListingAsync(string shop)
    {
        await using var scope = await ScopeAsync("dhaka");
        var merchant = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Merchants
            .AsNoTracking()
            .SingleAsync(m => m.Name == shop, Cancel);

        return merchant.ShopUrl is null ? null : (merchant.ShopUrl, merchant.ShopAbout);
    }

    private async Task<IReadOnlyList<string>> OtherListedAsync(string slug, string shop)
    {
        await using var scope = await ScopeAsync(slug);

        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Merchants
            .Where(m => m.ShopUrl != null && m.Name != shop)
            .Select(m => m.Name)
            .ToListAsync(Cancel);
    }

    private async Task LeaveAsync(string slug, params string[] shops)
    {
        await using var scope = await ScopeAsync(slug);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var merchant in await db.Merchants.Where(m => shops.Contains(m.Name)).ToListAsync(Cancel))
        {
            merchant.LeaveWindow();
        }

        await db.SaveChangesAsync(Cancel);
    }

    /// <summary>Writes the order's "placed" text as the sender would, into a sender of this test's own.</summary>
    private async Task<SentSms> TextAsync(string number, string slug = "dhaka")
    {
        await using var scope = await ScopeAsync(slug);
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<AppDbContext>();
        var orderId = await db.Orders.IgnoreQueryFilters([AppDbContext.MerchantFilter])
            .Where(order => order.Number == number)
            .Select(order => order.Id)
            .SingleAsync(Cancel);
        var payload = JsonSerializer.Serialize(new OrderPlacedMessage(orderId));
        var message = await db.OutboxMessages.SingleAsync(
            m => m.Type == nameof(OrderPlacedMessage) && m.Payload == payload,
            Cancel);
        var sms = new RecordingSms();
        await ActivatorUtilities.CreateInstance<CustomerTexts>(services, (ISmsSender)sms).SendAsync(message, Cancel);

        return Assert.Single(sms.Sent);
    }

    /// <summary>A shop of this test's own (Mirpur 1, or Chattogram's Agrabad), listed with <see cref="Url"/> or not.</summary>
    private async Task<(string Name, string ApiKey)> NewShopAsync(string slug, bool list)
    {
        await using var scope = await ScopeAsync(slug);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var name = $"Window Shop {Guid.NewGuid():N}"[..20];
        var area = await db.Areas.SingleAsync(a => a.Name == (slug == "dhaka" ? "Mirpur 1" : "Agrabad"), Cancel);
        var merchant = new Merchant(name, area.ZoneId, "01711999999", null);
        if (list)
        {
            Assert.True(merchant.ListInWindow(Url(name), "Test kantha").IsSuccess);
        }

        db.Merchants.Add(merchant);
        await db.SaveChangesAsync(Cancel);

        var (key, plaintext) = MerchantApiKey.Issue(merchant.Id, "Window test");
        db.PickupPoints.Add(new PickupPoint(merchant.Id, area.Id, "Shop", $"{name}, {area.Name}", "01711999999", isDefault: true));
        db.MerchantApiKeys.Add(key);
        await db.SaveChangesAsync(Cancel);

        return (name, plaintext);
    }

    /// <summary>An order paid online (no confirmation asked), waiting for other shops or sent fast.</summary>
    private async Task<string> CreateAsync(string apiKey, string phone, string speed, string area = "Mirpur 10")
    {
        var order = new
        {
            Customer = new { Name = "Window Customer", Phone = phone },
            Address = new { Area = area, Line1 = "House 5, Road 7" },
            Packages = new[] { new { Description = "Parcel", WeightGrams = 400 } },
            CodAmount = 0,
            Speed = speed
        };
        var response = await factory.ClientFor(apiKey).PostAsJsonAsync("/api/v1/orders", order, Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<Created>(Json, Cancel))!.Number;
    }

    /// <summary>Posts one of the page's forms and returns the page it lands on.</summary>
    private static async Task<string> PostPageAsync(
        HttpClient client,
        string page,
        string handler,
        params (string Name, string Value)[] fields)
    {
        var token = Token().Match(await client.GetStringAsync(page, Cancel)).Groups[1].Value;
        var form = new FormUrlEncodedContent(
        [
            .. fields.Select(field => KeyValuePair.Create(field.Name, field.Value)),
            KeyValuePair.Create("__RequestVerificationToken", token)
        ]);
        var response = await client.PostAsync($"{page}?handler={handler}", form, Cancel);
        if (response.StatusCode == HttpStatusCode.Redirect)
        {
            return WebUtility.HtmlDecode(await client.GetStringAsync(response.Headers.Location!.OriginalString, Cancel));
        }

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Cancel));
    }

    /// <summary>Signs a demo shop in on Dhaka's subdomain. Keeps the sign-in cookie.</summary>
    private async Task<HttpClient> SignInAsync(string email)
    {
        var client = Client("dhaka");
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

    /// <summary>A browser on an operator's subdomain, or on the platform's own address.</summary>
    private HttpClient Client(string? slug)
    {
        return factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(slug is null ? "http://localhost" : $"http://{slug}.localhost"),
            AllowAutoRedirect = false
        });
    }

    private async Task<AsyncServiceScope> ScopeAsync(string slug)
    {
        var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(await TenantAsync(slug));

        return scope;
    }

    private async Task<TenantInfo> TenantAsync(string slug)
    {
        return (await factory.Services.GetRequiredService<ITenantCatalog>().FindBySlugAsync(slug, Cancel))!;
    }

    private static string NewPhone()
    {
        return "016" + Random.Shared.Next(0, 100_000_000).ToString("D8");
    }

    [GeneratedRegex("""name="__RequestVerificationToken" type="hidden" value="([^"]+)""")]
    private static partial Regex Token();

    private sealed record Created(string Number);

    private sealed class RecordingSms : ISmsSender
    {
        private readonly ConcurrentQueue<SentSms> sent = new();

        public IReadOnlyCollection<SentSms> Sent => sent;

        public Task SendAsync(PhoneNumber to, string senderName, string text, CancellationToken cancellationToken = default)
        {
            sent.Enqueue(new SentSms(DateTime.UtcNow, to.Value, senderName, text));

            return Task.CompletedTask;
        }
    }
}
