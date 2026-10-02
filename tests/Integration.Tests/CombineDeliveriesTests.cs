using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Application.Abstractions;
using Application.Grouping.CombineDeliveries;
using Application.Grouping.CustomerDeliveries;
using Application.Network.HubScan;
using Application.Notifications;
using Application.Notifications.SendOutbox;
using Application.Orders.ConfirmOrder;
using Domain.Customers;
using Domain.Grouping;
using Domain.Merchants;
using Domain.Payments;
using Infrastructure.MultiTenancy;
using Infrastructure.Payments;
using Infrastructure.Persistence;
using Infrastructure.Sms;

namespace Integration.Tests;

/// <summary>
/// Task 4.7: a customer with deliveries on their way to two spellings of one address in an area is asked whether it is
/// the same place. Combining makes one delivery and learns the spelling; keeping them apart stops the question. Shops
/// of the tests' own collect in Mirpur 1, and every customer is new.
/// </summary>
public partial class CombineDeliveriesTests(WebAppFactory factory)
{
    [Fact]
    public async Task Two_spellings_are_asked_about_and_combining_makes_one_delivery_that_learns_the_spelling()
    {
        WebAppFactory.RequireDatabase();
        var dhaka = await TenantAsync("dhaka");
        var fashion = await NewShopAsync();
        var gadgets = await NewShopAsync();
        var books = await NewShopAsync();
        var phone = NewPhone();
        var home = await CreateAsync(fashion.ApiKey, phone, "House 8, Road 3");
        var spelt = await CreateAsync(gadgets.ApiKey, phone, "Flat 2A, House 8, Road 3");
        var first = await DeliveryOfAsync(home.Number);
        var second = await DeliveryOfAsync(spelt.Number);

        // Two deliveries, each at the first-shop fee, and the second order's text asks with its link
        Assert.NotEqual(first.Number, second.Number);
        Assert.Equal((dhaka.BaseDeliveryFee, dhaka.BaseDeliveryFee), (home.Fee, spelt.Fee));
        Assert.Null(await TokenAsync(home.Number));
        var token = (await TokenAsync(spelt.Number))!;
        var text = await TextAsync(spelt.Number);
        Assert.Contains($"Same address as your delivery {first.Number} (House 8, Road 3, Mirpur 10)?", text.Text);
        Assert.Contains($"/Customer/Order?token={token}", text.Text);

        // "My deliveries" asks on the newer spelling's card
        var customerId = await CustomerIdAsync(phone);
        var before = await DeliveriesAsync(customerId);
        var asking = before.OnTheWay.Single(d => d.Number == second.Number).SameAddress!;
        Assert.Equal((first.Number, "House 8, Road 3, Mirpur 10"), (asking.OtherDelivery, asking.OtherAddress));
        Assert.Null(before.OnTheWay.Single(d => d.Number == first.Number).SameAddress);

        // Another operator cannot answer through the link
        Assert.Equal(
            "order.confirm.notFound",
            (await ConfirmAsync(handler => handler.AnswerSameAddressAsync(token, true, Cancel), "chattogram")).Error!.Code);

        var answered = await ConfirmAsync(handler => handler.AnswerSameAddressAsync(token, true, Cancel));

        Assert.Equal(first.Number, answered.Value.Delivery);
        Assert.Null(answered.Value.SameAddress);
        var kept = await DeliveryOfAsync(home.Number);
        Assert.Equal(first.Number, (await DeliveryOfAsync(spelt.Number)).Number);
        Assert.Equal(DeliveryGroupStatus.Cancelled, (await DeliveryAsync(second.Number)).Status);
        Assert.Equal((kept.AddressId, kept.AddressId), (await AddressOfAsync(home.Number), await AddressOfAsync(spelt.Number)));
        Assert.Equal(kept.AddressId, (await SpellingAsync(customerId, "Flat 2A, House 8, Road 3")).SameAsId);

        // One delivery for one fee; the merchant's fee for its order is what it was told
        var after = await DeliveriesAsync(customerId);
        var combined = Assert.Single(after.OnTheWay);
        Assert.Equal(first.Number, combined.Number);
        Assert.Equal(dhaka.BaseDeliveryFee + dhaka.ExtraShopFee, combined.Fee);
        Assert.Null(combined.SameAddress);
        Assert.DoesNotContain(after.Earlier, d => d.Number == second.Number);
        Assert.Equal(dhaka.BaseDeliveryFee, (await GetAsync(gadgets.ApiKey, spelt.Number)).Fee);

        // The spelling is learnt: the next order typed that way joins by itself and asks nothing
        var quote = await QuoteAsync(books.ApiKey, phone, "Flat 2A, House 8, Road 3");
        var third = await CreateAsync(books.ApiKey, phone, "Flat 2A, House 8, Road 3");
        Assert.Equal((dhaka.ExtraShopFee, true), (quote.Fee, quote.JoinsDelivery));
        Assert.Equal(dhaka.ExtraShopFee, third.Fee);
        Assert.Equal(first.Number, (await DeliveryOfAsync(third.Number)).Number);
        Assert.Null(await TokenAsync(third.Number));
        Assert.DoesNotContain("Same address", (await TextAsync(third.Number)).Text);
    }

    [Fact]
    public async Task Keeping_apart_stops_the_question_and_other_areas_are_never_asked()
    {
        WebAppFactory.RequireDatabase();
        var shop = await NewShopAsync();
        var other = await NewShopAsync();
        var phone = NewPhone();
        var home = await CreateAsync(shop.ApiKey, phone, "House 12, Road 5");
        var neighbour = await CreateAsync(other.ApiKey, phone, "House 12/A, Road 5");
        var office = await CreateAsync(other.ApiKey, phone, "House 12, Road 5", area: "Mirpur 2");
        var customerId = await CustomerIdAsync(phone);
        var neighbourDelivery = (await DeliveryOfAsync(neighbour.Number)).Number;

        // Only the pair in one area is asked about; the office in another area never is
        var question = Assert.Single(await QuestionsAsync(customerId));
        Assert.Equal((neighbourDelivery, (await DeliveryOfAsync(home.Number)).Number), (question.Delivery, question.OtherDelivery));
        Assert.Null(await TokenAsync(office.Number));

        // Someone else's delivery is not found, and saying no settles the address
        var stranger = await CustomerIdAsync(await NewCustomerAsync(shop.ApiKey));
        Assert.Equal("deliveryGroup.notFound", (await CombineAsync(h => h.KeepSeparateAsync(stranger, neighbourDelivery, Cancel))).Error!.Code);
        Assert.True((await CombineAsync(h => h.KeepSeparateAsync(customerId, neighbourDelivery, Cancel))).IsSuccess);
        Assert.True((await CombineAsync(h => h.KeepSeparateAsync(customerId, neighbourDelivery, Cancel))).IsSuccess);

        Assert.Empty(await QuestionsAsync(customerId));
        Assert.NotNull((await SpellingAsync(customerId, "House 12/A, Road 5")).KeptApartOn);
        Assert.Equal(
            DeliveryGroup.NotCombinable.Code,
            (await CombineAsync(h => h.CombineAsync(customerId, neighbourDelivery, Cancel))).Error!.Code);

        // The next order to that address travels in its own delivery and is not asked about again
        var again = await CreateAsync(shop.ApiKey, phone, "House 12/A, Road 5");
        Assert.Equal(neighbourDelivery, (await DeliveryOfAsync(again.Number)).Number);
        Assert.Null(await TokenAsync(again.Number));
    }

    [Fact]
    public async Task The_kept_delivery_takes_the_cancelled_ones_shelf_and_advance()
    {
        WebAppFactory.RequireDatabase();
        var dhaka = await TenantAsync("dhaka");
        var shop = await NewShopAsync();
        var asking = await NewShopAsync();
        var phone = NewPhone();
        var home = await CreateAsync(shop.ApiKey, phone, "House 30, Road 9");
        var spelt = await CreateAsync(asking.ApiKey, phone, "H 30 R 9 Flat 6", cod: 800, feeInAdvance: true);
        var token = (await TokenAsync(spelt.Number))!;
        var first = await DeliveryOfAsync(home.Number);
        var second = await DeliveryOfAsync(spelt.Number);

        // The advance is paid and the second delivery's parcel is on a shelf at the hub
        await ConfirmAsync(handler => handler.RequestAdvanceAsync(token, PaymentMethod.Bkash, Cancel));
        var advance = Assert.Single(await AdvancesAsync(second.Id));
        factory.Services.GetRequiredService<FakePaymentLog>().Pay(advance.GatewayReference!, DateTime.UtcNow);
        Assert.True((await ConfirmAsync(handler => handler.CheckAdvanceAsync(token, Cancel))).Value.Done);
        Assert.True((await ScanAsync(h => h.CollectAsync($"{spelt.Number}-1", Cancel))).IsSuccess);
        Assert.True((await ScanAsync(h => h.ReceiveAsync("MIR", $"{spelt.Number}-1", Cancel))).IsSuccess);
        var shelf = (await DeliveryAsync(second.Number)).Shelf;
        Assert.NotNull(shelf);

        var customerId = await CustomerIdAsync(phone);
        var combined = await CombineAsync(h => h.CombineAsync(customerId, first.Number, Cancel));

        // The older delivery goes on, on the freed shelf, with the advance coming off its fee at the door
        Assert.Equal(first.Number, combined.Value);
        var kept = await DeliveryAsync(first.Number);
        var cancelled = await DeliveryAsync(second.Number);
        Assert.Equal((shelf, DeliveryGroupStatus.Open), (kept.Shelf, kept.Status));
        Assert.Equal((null, DeliveryGroupStatus.Cancelled), (cancelled.Shelf, cancelled.Status));
        var moved = Assert.Single(await AdvancesAsync(first.Id));
        Assert.Equal((PaymentStatus.Paid, dhaka.BaseDeliveryFee), (moved.Status, moved.Fee));
        Assert.Empty(await AdvancesAsync(second.Id));
        Assert.Equal(first.Number, (await DeliveryOfAsync(spelt.Number)).Number);
    }

    [Fact]
    public async Task The_page_the_sms_links_to_asks_and_combines()
    {
        WebAppFactory.RequireDatabase();
        var shop = await NewShopAsync();
        var other = await NewShopAsync();
        var phone = NewPhone();
        var home = await CreateAsync(shop.ApiKey, phone, "House 44, Road 2");
        var spelt = await CreateAsync(other.ApiKey, phone, "Flat B1, House 44, Road 2");
        var first = (await DeliveryOfAsync(home.Number)).Number;
        var page = $"/Customer/Order?token={await TokenAsync(spelt.Number)}";
        var client = factory.CreateClient(Options("dhaka"));

        var asked = WebUtility.HtmlDecode(await client.GetStringAsync(page, Cancel));
        var answer = await PostAsync(client, page, "SameAddress", ("same", "true"));
        var shown = WebUtility.HtmlDecode(await client.GetStringAsync(page, Cancel));

        Assert.Contains($"Same address as your delivery {first}?", asked);
        Assert.Contains("House 44, Road 2, Mirpur 10", asked);
        Assert.Contains("Same address: combine</button>", asked);
        Assert.Contains($"Combined: everything now travels in delivery {first}, for one fee.", answer);
        Assert.DoesNotContain("Same address as your delivery", shown);
        Assert.Contains("We deliver it on", shown);
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private async Task<T> ConfirmAsync<T>(Func<ConfirmOrderHandler, Task<T>> act, string slug = "dhaka")
    {
        await using var scope = await ScopeAsync(slug);

        return await act(scope.ServiceProvider.GetRequiredService<ConfirmOrderHandler>());
    }

    private async Task<T> CombineAsync<T>(Func<CombineDeliveriesHandler, Task<T>> act)
    {
        await using var scope = await ScopeAsync("dhaka");

        return await act(scope.ServiceProvider.GetRequiredService<CombineDeliveriesHandler>());
    }

    private async Task<T> ScanAsync<T>(Func<HubScanHandler, Task<T>> act)
    {
        await using var scope = await ScopeAsync("dhaka");

        return await act(scope.ServiceProvider.GetRequiredService<HubScanHandler>());
    }

    private Task<IReadOnlyList<SameAddressQuestion>> QuestionsAsync(long customerId)
    {
        return CombineAsync(h => h.QuestionsAsync(customerId, Cancel));
    }

    private async Task<CustomerDeliveries> DeliveriesAsync(long customerId)
    {
        await using var scope = await ScopeAsync("dhaka");

        return await scope.ServiceProvider.GetRequiredService<CustomerDeliveriesHandler>().HandleAsync(customerId, Cancel);
    }

    private async Task<DeliveryGroup> DeliveryOfAsync(string orderNumber)
    {
        await using var scope = await ScopeAsync("dhaka");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await (
            from order in db.Orders.IgnoreQueryFilters()
            join g in db.DeliveryGroups on order.DeliveryGroupId equals g.Id
            where order.Number == orderNumber
            select g)
            .AsNoTracking()
            .SingleAsync(Cancel);
    }

    private async Task<DeliveryGroup> DeliveryAsync(string number)
    {
        await using var scope = await ScopeAsync("dhaka");

        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().DeliveryGroups
            .AsNoTracking()
            .SingleAsync(g => g.Number == number, Cancel);
    }

    private async Task<long> AddressOfAsync(string orderNumber)
    {
        await using var scope = await ScopeAsync("dhaka");

        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Orders
            .IgnoreQueryFilters()
            .Where(order => order.Number == orderNumber)
            .Select(order => order.AddressId)
            .SingleAsync(Cancel);
    }

    private async Task<CustomerAddress> SpellingAsync(long customerId, string line1)
    {
        await using var scope = await ScopeAsync("dhaka");
        var matchKey = CustomerAddress.BuildMatchKey(line1, null);

        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().CustomerAddresses
            .AsNoTracking()
            .SingleAsync(a => a.CustomerId == customerId && a.MatchKey == matchKey, Cancel);
    }

    private async Task<IReadOnlyList<Payment>> AdvancesAsync(long deliveryGroupId)
    {
        await using var scope = await ScopeAsync("dhaka");

        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Payments
            .Where(p => p.DeliveryGroupId == deliveryGroupId && p.Purpose == PaymentPurpose.Advance)
            .AsNoTracking()
            .ToListAsync(Cancel);
    }

    private async Task<string?> TokenAsync(string number)
    {
        await using var scope = await ScopeAsync("dhaka");

        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Orders
            .IgnoreQueryFilters()
            .Where(order => order.Number == number)
            .Select(order => order.CustomerToken)
            .SingleAsync(Cancel);
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

    /// <summary>Writes the order's "placed" text as the sender would, into a sender of this test's own.</summary>
    private async Task<SentSms> TextAsync(string number)
    {
        await using var scope = await ScopeAsync("dhaka");
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<AppDbContext>();
        var orderId = await db.Orders.IgnoreQueryFilters()
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

    /// <summary>A shop of this test's own, collected from Mirpur 1.</summary>
    private async Task<(string Name, string ApiKey)> NewShopAsync()
    {
        await using var scope = await ScopeAsync("dhaka");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var name = $"Combine Shop {Guid.NewGuid():N}"[..20];
        var area = await db.Areas.SingleAsync(a => a.Name == "Mirpur 1", Cancel);
        var merchant = new Merchant(name, area.ZoneId, "01711999999", null);
        db.Merchants.Add(merchant);
        await db.SaveChangesAsync(Cancel);

        var (key, plaintext) = MerchantApiKey.Issue(merchant.Id, "Combine test");
        db.PickupPoints.Add(new PickupPoint(merchant.Id, area.Id, "Shop", $"{name}, Mirpur 1", "01711999999", isDefault: true));
        db.MerchantApiKeys.Add(key);
        await db.SaveChangesAsync(Cancel);

        return (name, plaintext);
    }

    private async Task<string> NewCustomerAsync(string apiKey)
    {
        var phone = NewPhone();
        await CreateAsync(apiKey, phone, "House 1, Road 1");

        return phone;
    }

    /// <summary>An order paid online (no confirmation asked) unless <paramref name="cod"/> says otherwise.</summary>
    private async Task<PlacedOrder> CreateAsync(
        string apiKey,
        string phone,
        string line1,
        string area = "Mirpur 10",
        decimal cod = 0,
        bool feeInAdvance = false)
    {
        var order = new
        {
            Customer = new { Name = "Combine Customer", Phone = phone },
            Address = new { Area = area, Line1 = line1 },
            Packages = new[] { new { Description = "Parcel", WeightGrams = 400 } },
            CodAmount = cod,
            Speed = "combine",
            FeeInAdvance = feeInAdvance
        };
        var response = await factory.ClientFor(apiKey).PostAsJsonAsync("/api/v1/orders", order, Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<PlacedOrder>(Json, Cancel))!;
    }

    private async Task<PlacedOrder> GetAsync(string apiKey, string number)
    {
        return (await factory.ClientFor(apiKey).GetFromJsonAsync<PlacedOrder>($"/api/v1/orders/{number}", Json, Cancel))!;
    }

    private async Task<Quote> QuoteAsync(string apiKey, string phone, string line1)
    {
        var url = $"/api/v1/quote?phone={Uri.EscapeDataString(phone)}&area={Uri.EscapeDataString("Mirpur 10")}" +
            $"&line1={Uri.EscapeDataString(line1)}";

        return (await factory.ClientFor(apiKey).GetFromJsonAsync<Quote>(url, Json, Cancel))!;
    }

    private static async Task<string> PostAsync(HttpClient client, string page, string handler, params (string Name, string Value)[] fields)
    {
        var token = Token().Match(await client.GetStringAsync(page, Cancel)).Groups[1].Value;
        var form = new FormUrlEncodedContent(
        [
            .. fields.Select(field => KeyValuePair.Create(field.Name, field.Value)),
            KeyValuePair.Create("__RequestVerificationToken", token)
        ]);
        var response = await client.PostAsync($"{page}&handler={handler}", form, Cancel);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Cancel));
    }

    private static WebApplicationFactoryClientOptions Options(string slug)
    {
        return new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri($"http://{slug}.localhost"),
            AllowAutoRedirect = false
        };
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
        return "018" + Random.Shared.Next(0, 100_000_000).ToString("D8");
    }

    [GeneratedRegex("""name="__RequestVerificationToken" type="hidden" value="([^"]+)""")]
    private static partial Regex Token();

    private sealed record PlacedOrder(string Number, decimal Fee);

    private sealed record Quote(decimal Fee, bool JoinsDelivery);

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
