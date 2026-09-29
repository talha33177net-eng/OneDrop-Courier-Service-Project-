using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Application.Abstractions;
using Domain.Customers;
using Domain.Grouping;
using Infrastructure.MultiTenancy;
using Infrastructure.Persistence;
using Infrastructure.Sms;

namespace Integration.Tests;

/// <summary>
/// Task 2.6: the customer closes an open delivery early, from the API or the page, and it goes out the next day.
/// Customers sign in for real (SMS code), on their operator's subdomain. The phone login page allows 10 requests
/// a minute for the whole test run, so each sign-in spends two of them and only four tests sign in.
/// </summary>
public partial class ShipNowTests(WebAppFactory factory)
{
    private const string Area = "Mirpur 10";
    private const string Home = "House 21, Road 4";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [Fact]
    public async Task The_owner_ships_now_pays_the_fast_difference_and_the_delivery_goes_out_tomorrow_still_open_to_shops()
    {
        WebAppFactory.RequireDatabase();
        var dhaka = await TenantAsync("dhaka");
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(dhaka.TimeZone);
        var phone = NewPhone();
        await CreateAsync(WebAppFactory.DhakaFashion, phone);
        await CreateAsync(WebAppFactory.DhakaGadget, phone);
        var group = await OpenGroupAsync("dhaka", phone);
        var client = await SignInAsync("dhaka", phone);

        var response = await client.PostAsync($"/api/v1/deliveries/{group.Number}/ship-now", null, Cancel);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tomorrow = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone)).AddDays(1);
        Assert.Equal(
            new Shipped(group.Number, tomorrow, dhaka.FastDeliveryFee - dhaka.BaseDeliveryFee),
            await response.Content.ReadFromJsonAsync<Shipped>(Json, Cancel));

        var shipped = await GroupAsync("dhaka", group.Id);
        Assert.Equal(DeliveryGroupStatus.Locked, shipped.Status);
        Assert.NotNull(shipped.LockedOn);
        Assert.True(shipped.LocksAt < group.LocksAt);
        Assert.Equal(tomorrow, DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(shipped.LocksAt, timeZone)));

        var again = await client.PostAsync($"/api/v1/deliveries/{group.Number}/ship-now", null, Cancel);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Contains(DeliveryGroup.NotOpenForShipNow.Code, await again.Content.ReadAsStringAsync(Cancel));

        // A shop whose pickup route still runs by tomorrow joins the shipped delivery at the extra-shop fee;
        // no new delivery opens
        Assert.Equal(dhaka.ExtraShopFee, await CreateAsync(WebAppFactory.DhakaBeauty, phone));
        Assert.Null(await OpenGroupOrNullAsync("dhaka", phone));
    }

    [Fact]
    public async Task Another_customers_delivery_is_not_found_from_the_api_or_the_page()
    {
        WebAppFactory.RequireDatabase();
        var owner = NewPhone();
        await CreateAsync(WebAppFactory.DhakaFashion, owner);
        var group = await OpenGroupAsync("dhaka", owner);
        var client = await SignInAsync("dhaka", NewPhone());

        var api = await client.PostAsync($"/api/v1/deliveries/{group.Number}/ship-now", null, Cancel);
        var page = await PostShipNowPageAsync(client, group.Number);

        Assert.Equal(HttpStatusCode.NotFound, api.StatusCode);
        Assert.Contains($"Delivery {group.Number} was not found.", page);
        Assert.Equal(DeliveryGroupStatus.Open, (await GroupAsync("dhaka", group.Id)).Status);
    }

    [Fact]
    public async Task The_same_phone_signed_in_with_another_operator_cannot_ship_the_delivery()
    {
        WebAppFactory.RequireDatabase();
        var phone = NewPhone();
        await CreateAsync(WebAppFactory.DhakaFashion, phone);
        var group = await OpenGroupAsync("dhaka", phone);
        var chattogram = await SignInAsync("chattogram", phone);

        var response = await chattogram.PostAsync($"/api/v1/deliveries/{group.Number}/ship-now", null, Cancel);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(DeliveryGroupStatus.Open, (await GroupAsync("dhaka", group.Id)).Status);
    }

    [Fact]
    public async Task The_page_shows_the_waiting_delivery_as_an_installable_app_and_Ship_now_closes_it()
    {
        WebAppFactory.RequireDatabase();
        var dhaka = await TenantAsync("dhaka");
        var phone = NewPhone();
        await CreateAsync(WebAppFactory.DhakaFashion, phone);
        await CreateAsync(WebAppFactory.DhakaBeauty, phone);
        var group = await OpenGroupAsync("dhaka", phone);
        var client = await SignInAsync("dhaka", phone);

        var before = await client.GetStringAsync("/Customer", Cancel);
        var after = await PostShipNowPageAsync(client, group.Number);

        // Two shops' orders, each with COD 500, at the tenant's prices
        var fee = dhaka.BaseDeliveryFee + dhaka.ExtraShopFee;
        Assert.Contains("Waiting for more shops", before);
        Assert.Contains(group.Number, before);
        Assert.Contains($"To {Home}, {Area}.", before);
        Assert.Contains("<strong>Beauty Shop</strong>", before);
        Assert.Contains("<strong>Fashion House</strong>", before);
        Assert.Contains("<strong>0 of 2</strong>", before);
        Assert.Contains($"৳{fee:N0}", before);
        Assert.Contains($"৳{fee + 1000:N0}", before);
        Assert.Contains($"You save ৳{2 * dhaka.BaseDeliveryFee - fee:N0} against 2 separate deliveries.", before);
        Assert.Contains("""<link rel="manifest" href="/manifest.webmanifest" />""", before);
        var shipNowFee = dhaka.FastDeliveryFee - dhaka.BaseDeliveryFee;
        Assert.Contains($"Deliver tomorrow for +৳{shipNowFee:N0}</button>", before);
        Assert.Contains($"Delivery {group.Number} is closed.", after);
        Assert.Contains("Its fee went up by", after);
        Assert.Contains($"৳{fee + shipNowFee:N0}", after);
        Assert.DoesNotContain("Waiting for more shops", after);
        Assert.DoesNotContain("Ship now</button>", after);
        Assert.DoesNotContain("Deliver tomorrow for", after);
        Assert.Equal(DeliveryGroupStatus.Locked, (await GroupAsync("dhaka", group.Id)).Status);
    }

    [Fact]
    public async Task Ship_now_needs_a_signed_in_customer_not_a_redirect_or_an_api_key()
    {
        WebAppFactory.RequireDatabase();
        var phone = NewPhone();
        await CreateAsync(WebAppFactory.DhakaFashion, phone);
        var group = await OpenGroupAsync("dhaka", phone);
        var anonymous = factory.CreateClient(Options("dhaka"));
        var merchant = factory.ClientFor(WebAppFactory.DhakaFashion);

        var noLogin = await anonymous.PostAsync($"/api/v1/deliveries/{group.Number}/ship-now", null, Cancel);
        var apiKey = await merchant.PostAsync($"/api/v1/deliveries/{group.Number}/ship-now", null, Cancel);

        Assert.Equal(HttpStatusCode.Unauthorized, noLogin.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, apiKey.StatusCode);
        Assert.Equal(DeliveryGroupStatus.Open, (await GroupAsync("dhaka", group.Id)).Status);
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static WebApplicationFactoryClientOptions Options(string slug)
    {
        return new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri($"http://{slug}.localhost"),
            AllowAutoRedirect = false
        };
    }

    /// <summary>Signs in with the SMS code the fake sender "sent". Keeps the sign-in cookie.</summary>
    private async Task<HttpClient> SignInAsync(string slug, string phone)
    {
        var client = factory.CreateClient(Options(slug));

        // The staff login page is not rate limited; its form carries the anti-forgery token for the phone login
        var token = Token(await client.GetStringAsync("/Account/Login", Cancel));
        var sent = await PostFormAsync(client, "/Account/PhoneLogin?handler=Send", token, new() { ["Phone"] = phone });
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);

        var e164 = PhoneNumber.Parse(phone).Value.Value;
        var sms = factory.Services.GetRequiredService<SmsLog>().Recent.First(message => message.To == e164);
        var code = SixDigits().Match(sms.Text).Value;
        var verified = await PostFormAsync(
            client,
            "/Account/PhoneLogin?handler=Verify",
            token,
            new() { ["Phone"] = phone, ["Code"] = code });
        Assert.Equal(HttpStatusCode.Redirect, verified.StatusCode);

        return client;
    }

    /// <summary>Presses Ship now on the customer page and returns the page it redirects back to.</summary>
    private async Task<string> PostShipNowPageAsync(HttpClient client, string number)
    {
        var token = Token(await client.GetStringAsync("/Customer", Cancel));
        var posted = await PostFormAsync(client, "/Customer?handler=ShipNow", token, new() { ["number"] = number });
        Assert.Equal(HttpStatusCode.Redirect, posted.StatusCode);

        return await client.GetStringAsync(posted.Headers.Location, Cancel);
    }

    private static Task<HttpResponseMessage> PostFormAsync(
        HttpClient client,
        string url,
        string token,
        Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = token;

        return client.PostAsync(url, new FormUrlEncodedContent(fields), Cancel);
    }

    private static string Token(string html)
    {
        return AntiforgeryToken().Match(html).Groups[1].Value;
    }

    private async Task<decimal> CreateAsync(string apiKey, string phone)
    {
        var order = new
        {
            Customer = new { Name = "Ship Now Customer", Phone = phone },
            Address = new { Area, Line1 = Home },
            Packages = new[] { new { Description = "Parcel", WeightGrams = 400 } },
            CodAmount = 500
        };
        var response = await factory.ClientFor(apiKey).PostAsJsonAsync("/api/v1/orders", order, Json, Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<Priced>(Json, Cancel))!.Fee;
    }

    private async Task<DeliveryGroup> OpenGroupAsync(string slug, string phone)
    {
        return await OpenGroupOrNullAsync(slug, phone) ?? throw new InvalidOperationException("No open delivery.");
    }

    private async Task<DeliveryGroup?> OpenGroupOrNullAsync(string slug, string phone)
    {
        await using var scope = await ScopeAsync(slug);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var e164 = PhoneNumber.Parse(phone).Value.Value;
        var customerId = await db.Customers.Where(c => c.Phone == e164).Select(c => c.Id).SingleAsync(Cancel);

        return await db.DeliveryGroups
            .AsNoTracking()
            .SingleOrDefaultAsync(g => g.CustomerId == customerId && g.Status == DeliveryGroupStatus.Open, Cancel);
    }

    private async Task<DeliveryGroup> GroupAsync(string slug, long id)
    {
        await using var scope = await ScopeAsync(slug);

        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().DeliveryGroups
            .AsNoTracking()
            .SingleAsync(g => g.Id == id, Cancel);
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
        return "019" + Random.Shared.Next(0, 100_000_000).ToString("D8");
    }

    [GeneratedRegex("""name="__RequestVerificationToken" type="hidden" value="([^"]+)""")]
    private static partial Regex AntiforgeryToken();

    [GeneratedRegex(@"\b\d{6}\b")]
    private static partial Regex SixDigits();

    private sealed record Shipped(string Number, DateOnly DeliveryDate, decimal AddedFee);

    private sealed record Priced(decimal Fee);
}
