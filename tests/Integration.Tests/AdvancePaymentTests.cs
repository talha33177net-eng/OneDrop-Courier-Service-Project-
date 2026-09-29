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
using Application.Network.HubScan;
using Application.Network.PickupRoutes;
using Application.Notifications;
using Application.Notifications.SendOutbox;
using Application.Orders.ConfirmOrder;
using Domain.Customers;
using Domain.Merchants;
using Domain.Orders;
using Domain.Payments;
using Infrastructure.MultiTenancy;
using Infrastructure.Payments;
using Infrastructure.Persistence;
using Infrastructure.Sms;

namespace Integration.Tests;

/// <summary>
/// Task 3.6b: a new customer confirms their first cash order with one tap, and a customer at risk pays the delivery
/// fee in advance before their parcel leaves the shop. The tests open shops of their own in Uttara, so they join the
/// collection that keeps the pickup-route classes apart.
/// </summary>
[Collection("Uttara pickups")]
public partial class AdvancePaymentTests(WebAppFactory factory)
{
    private const string Password = "OneDrop#2026";

    [Fact]
    public async Task A_new_cash_customer_confirms_with_one_tap_and_the_shop_is_warned_until_they_do()
    {
        WebAppFactory.RequireDatabase();
        var shop = await NewShopAsync("Uttara Sector 7");
        var phone = NewPhone();
        var confirming = await CreateAsync(shop.ApiKey, phone, cod: 1200);
        var paidOnline = await CreateAsync(shop.ApiKey, NewPhone(), cod: 0);

        Assert.Equal(CustomerStep.Confirm, confirming.WaitsFor);
        Assert.Equal(CustomerStep.None, paidOnline.WaitsFor);

        // The SMS asks, with the link, and says no more than the shop already knows
        var text = await TextAsync(confirming.Number);
        var token = await TokenAsync(confirming.Number);
        Assert.Contains($"Is your {shop.Name} order {confirming.Number} correct?", text.Text);
        Assert.Contains($"http://dhaka.localhost:5080/Customer/Order?token={token}", text.Text);

        // On the sheet the collector sees the warning but still takes the parcel
        var before = await SheetOrderAsync("Uttara", shop.Name, confirming.Number);
        Assert.Equal((CustomerStep.Confirm, true), (before.Waits, before.Collect));

        var opened = await ConfirmAsync(handler => handler.FindAsync(token, Cancel));
        var confirmed = await ConfirmAsync(handler => handler.ConfirmAsync(token, Cancel));
        var again = await ConfirmAsync(handler => handler.ConfirmAsync(token, Cancel));
        var after = await SheetOrderAsync("Uttara", shop.Name, confirming.Number);

        Assert.Equal((shop.Name, confirming.Number, CustomerStep.Confirm, false), (opened.Value.Shop, opened.Value.Number, opened.Value.Step, opened.Value.Done));
        Assert.True(confirmed.Value.Done);
        Assert.True(again.Value.Done);
        Assert.Equal((CustomerStep.None, true), (after.Waits, after.Collect));
        Assert.Equal(OrderStatus.PickedUp, (await CollectAsync($"{confirming.Number}-1")).Value.Status);

        // An unknown link, and another operator's order, are simply not found
        Assert.Equal("order.confirm.notFound", (await ConfirmAsync(handler => handler.FindAsync("nothing", Cancel))).Error!.Code);
        Assert.Equal(
            "order.confirm.notFound",
            (await ConfirmAsync(handler => handler.FindAsync(token, Cancel), "chattogram")).Error!.Code);
    }

    [Fact]
    public async Task A_shop_that_asks_for_the_fee_in_advance_keeps_its_parcel_until_the_wallet_pays()
    {
        WebAppFactory.RequireDatabase();
        var dhaka = await TenantAsync("dhaka");
        var shop = await NewShopAsync("Uttara Sector 7");
        var otherShop = await NewShopAsync("Uttara Sector 10");
        var phone = NewPhone();
        var order = await CreateAsync(shop.ApiKey, phone, cod: 1500, feeInAdvance: true);
        var token = await TokenAsync(order.Number);

        Assert.Equal(CustomerStep.PayInAdvance, order.WaitsFor);
        Assert.Contains("goes out once the OneDrop delivery fee is paid", (await TextAsync(order.Number)).Text);

        // Not collected: the sheet lists it with the reason and leaves it out of the parcels to take
        var waiting = await SheetOrderAsync("Uttara", shop.Name, order.Number);
        var stop = await SheetStopAsync("Uttara", shop.Name);
        Assert.Equal((CustomerStep.PayInAdvance, false), (waiting.Waits, waiting.Collect));
        Assert.Equal(0, stop.Packages);
        Assert.Equal("order.scan.advance", (await CollectAsync($"{order.Number}-1")).Error!.Code);

        var cash = await ConfirmAsync(handler => handler.RequestAdvanceAsync(token, PaymentMethod.Cash, Cancel));
        var confirmOnly = await ConfirmAsync(handler => handler.ConfirmAsync(token, Cancel));
        var asked = await ConfirmAsync(handler => handler.RequestAdvanceAsync(token, PaymentMethod.Bkash, Cancel));
        var sameAgain = await ConfirmAsync(handler => handler.RequestAdvanceAsync(token, PaymentMethod.Bkash, Cancel));
        var unpaid = await ConfirmAsync(handler => handler.CheckAdvanceAsync(token, Cancel));

        // The advance is the delivery's first-shop fee, whatever other shops add at the door
        Assert.Equal("order.advance.method", cash.Error!.Code);
        Assert.Equal("order.confirm.advance", confirmOnly.Error!.Code);
        Assert.Equal((dhaka.BaseDeliveryFee, PaymentMethod.Bkash), (asked.Value.Fee, asked.Value.Method!.Value));
        Assert.Equal(asked.Value.PaymentLink, sameAgain.Value.PaymentLink);
        Assert.Equal("order.advance.unpaid", unpaid.Error!.Code);
        Assert.False(asked.Value.Done);

        // Switching wallet drops the first request
        await ConfirmAsync(handler => handler.RequestAdvanceAsync(token, PaymentMethod.Nagad, Cancel));
        var requests = await AdvancesAsync(order.Number);
        Assert.Equal(
            [(PaymentMethod.Bkash, PaymentStatus.Cancelled), (PaymentMethod.Nagad, PaymentStatus.Pending)],
            requests.Select(payment => (payment.Method, payment.Status)));

        // A second shop's order joins the same delivery and waits with it
        var joining = await CreateAsync(otherShop.ApiKey, phone, cod: 900);
        Assert.Equal(CustomerStep.PayInAdvance, joining.WaitsFor);
        Assert.Equal("order.scan.advance", (await CollectAsync($"{joining.Number}-1")).Error!.Code);

        factory.Services.GetRequiredService<FakePaymentLog>().Pay(requests[1].GatewayReference!, DateTime.UtcNow);
        var paid = await ConfirmAsync(handler => handler.CheckAdvanceAsync(token, Cancel));

        Assert.True(paid.Value.Done);
        Assert.Equal(dhaka.BaseDeliveryFee, Assert.Single(await AdvancesAsync(order.Number), p => p.Status == PaymentStatus.Paid).Fee);
        Assert.Equal(OrderStatus.PickedUp, (await CollectAsync($"{order.Number}-1")).Value.Status);

        // The whole delivery is paid for, so the shop that joined may send its parcel too
        Assert.Equal(OrderStatus.PickedUp, (await CollectAsync($"{joining.Number}-1")).Value.Status);

        // A third shop ordering after the advance is paid waits for nothing
        var later = await CreateAsync(shop.ApiKey, phone, cod: 300);
        Assert.Equal(CustomerStep.None, later.WaitsFor);
        Assert.Equal(dhaka.BaseDeliveryFee, Assert.Single(await AdvancesAsync(order.Number), p => p.Status == PaymentStatus.Paid).Fee);
    }

    [Fact]
    public async Task A_refusal_asks_for_the_fee_in_advance_at_every_shop_of_the_operator()
    {
        WebAppFactory.RequireDatabase();
        var shop = await NewShopAsync("Uttara Sector 7");
        var elsewhere = await NewShopAsync("Uttara Sector 10");
        var phone = NewPhone();
        var refused = await CreateAsync(shop.ApiKey, phone, cod: 1200);
        await RefuseAsync(refused.Number);

        // The other shop is not told why, only that its order waits
        var next = await CreateAsync(elsewhere.ApiKey, phone, cod: 700);
        var sameShop = await CreateAsync(shop.ApiKey, phone, cod: 500);

        Assert.Equal(CustomerStep.PayInAdvance, next.WaitsFor);
        Assert.Equal(CustomerStep.PayInAdvance, sameShop.WaitsFor);

        // The same phone at another operator is another customer and waits only to confirm
        var chattogram = await CreateAsync(
            WebAppFactory.ChattogramFashion,
            phone,
            cod: 700,
            area: "Agrabad",
            line1: "House 4, Road 1");
        Assert.Equal(CustomerStep.Confirm, chattogram.WaitsFor);
    }

    [Fact]
    public async Task The_page_the_sms_links_to_confirms_an_order_and_pays_an_advance()
    {
        WebAppFactory.RequireDatabase();
        var dhaka = await TenantAsync("dhaka");
        var shop = await NewShopAsync("Uttara Sector 7");
        var confirming = await CreateAsync(shop.ApiKey, NewPhone(), cod: 1200);
        var paying = await CreateAsync(shop.ApiKey, NewPhone(), cod: 1500, feeInAdvance: true);
        var client = factory.CreateClient(Options("dhaka"));

        var confirmPage = await client.GetStringAsync($"/Customer/Order?token={await TokenAsync(confirming.Number)}", Cancel);
        await PostAsync(client, $"/Customer/Order?token={await TokenAsync(confirming.Number)}", "Confirm");
        var confirmed = await client.GetStringAsync($"/Customer/Order?token={await TokenAsync(confirming.Number)}", Cancel);

        Assert.Contains("Yes, send it</button>", confirmPage);
        Assert.Contains(confirming.Number, confirmPage);
        Assert.Contains("your order is confirmed", confirmed);

        var payPage = await client.GetStringAsync($"/Customer/Order?token={await TokenAsync(paying.Number)}", Cancel);
        await PostAsync(client, $"/Customer/Order?token={await TokenAsync(paying.Number)}", "Pay", ("method", nameof(PaymentMethod.Bkash)));
        var withLink = await client.GetStringAsync($"/Customer/Order?token={await TokenAsync(paying.Number)}", Cancel);
        await PostAsync(client, $"/Customer/Order?token={await TokenAsync(paying.Number)}", "Check");
        var stillUnpaid = await client.GetStringAsync($"/Customer/Order?token={await TokenAsync(paying.Number)}", Cancel);
        var reference = Assert.Single(await AdvancesAsync(paying.Number)).GatewayReference!;
        factory.Services.GetRequiredService<FakePaymentLog>().Pay(reference, DateTime.UtcNow);
        await PostAsync(client, $"/Customer/Order?token={await TokenAsync(paying.Number)}", "Check");
        var done = await client.GetStringAsync($"/Customer/Order?token={await TokenAsync(paying.Number)}", Cancel);

        Assert.Contains($"delivery fee of <strong>৳{dhaka.BaseDeliveryFee:N0}</strong>", payPage);
        Assert.Contains("name=\"method\" value=\"Bkash\"", payPage);
        Assert.Contains("<svg", withLink);
        Assert.Contains("I have paid</button>", withLink);
        Assert.DoesNotContain("the delivery fee is paid", stillUnpaid);
        Assert.Contains("the delivery fee is paid", done);

        // A link that names no order is a 404, so a guessed one tells nobody anything
        var unknown = await client.GetAsync("/Customer/Order?token=guessed", Cancel);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
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

    private async Task<Domain.Common.Result<ParcelScan>> CollectAsync(string label, string slug = "dhaka")
    {
        await using var scope = await ScopeAsync(slug);

        return await scope.ServiceProvider.GetRequiredService<HubScanHandler>().CollectAsync(label, Cancel);
    }

    private async Task<PickupStop> SheetStopAsync(string zone, string shop)
    {
        await using var scope = await ScopeAsync("dhaka");
        var handler = scope.ServiceProvider.GetRequiredService<PickupRoutesHandler>();
        var routes = await handler.ListAsync(Cancel);
        var sheet = (await handler.GetAsync(routes.Single(route => route.Zone == zone).Id, Cancel))!;

        return sheet.Stops.Single(stop => stop.Merchant == shop);
    }

    private async Task<PickupStopOrder> SheetOrderAsync(string zone, string shop, string number)
    {
        return (await SheetStopAsync(zone, shop)).Orders.Single(order => order.Number == number);
    }

    /// <summary>The order's advance requests, oldest first.</summary>
    private async Task<IReadOnlyList<Payment>> AdvancesAsync(string number)
    {
        await using var scope = await ScopeAsync("dhaka");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await (
            from payment in db.Payments
            join order in db.Orders on payment.DeliveryGroupId equals order.DeliveryGroupId
            where order.Number == number && payment.Purpose == PaymentPurpose.Advance
            orderby payment.Id
            select payment)
            .AsNoTracking()
            .ToListAsync(Cancel);
    }

    private async Task<string> TokenAsync(string number)
    {
        await using var scope = await ScopeAsync("dhaka");

        return (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Orders
            .AsNoTracking()
            .SingleAsync(order => order.Number == number, Cancel))
            .CustomerToken!;
    }

    /// <summary>Writes the order's "placed" text as the sender would, into a sender of this test's own.</summary>
    private async Task<SentSms> TextAsync(string number)
    {
        await using var scope = await ScopeAsync("dhaka");
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<AppDbContext>();
        var orderId = await db.Orders.Where(order => order.Number == number).Select(order => order.Id).SingleAsync(Cancel);
        var payload = JsonSerializer.Serialize(new OrderPlacedMessage(orderId));
        var message = await db.OutboxMessages.SingleAsync(
            m => m.Type == nameof(OrderPlacedMessage) && m.Payload == payload,
            Cancel);
        var sms = new RecordingSms();
        await ActivatorUtilities.CreateInstance<CustomerTexts>(services, (ISmsSender)sms).SendAsync(message, Cancel);

        return Assert.Single(sms.Sent);
    }

    /// <summary>
    /// The customer refused the order at the door. Set in SQL: getting there for real needs a hub, a rider and a trip,
    /// which the trip tests cover.
    /// </summary>
    private async Task RefuseAsync(string number)
    {
        await using var scope = await ScopeAsync("dhaka");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [Orders].[Order] SET [Status] = {(byte)OrderStatus.Refused} WHERE [Number] = {number}",
            Cancel);
    }

    /// <summary>A shop of this test's own, with its default pickup point in <paramref name="area"/>.</summary>
    private async Task<(string Name, string ApiKey)> NewShopAsync(string area)
    {
        await using var scope = await ScopeAsync("dhaka");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var name = $"Advance Shop {Guid.NewGuid():N}"[..20];
        var pickupArea = await db.Areas.SingleAsync(a => a.Name == area, Cancel);
        var merchant = new Merchant(name, pickupArea.ZoneId, "01711999999", null);
        db.Merchants.Add(merchant);
        await db.SaveChangesAsync(Cancel);

        var (key, plaintext) = MerchantApiKey.Issue(merchant.Id, "Advance test");
        db.PickupPoints.Add(new PickupPoint(merchant.Id, pickupArea.Id, "Shop", $"{name}, {area}", "01711999999", isDefault: true));
        db.MerchantApiKeys.Add(key);
        await db.SaveChangesAsync(Cancel);

        return (name, plaintext);
    }

    private async Task<PlacedOrder> CreateAsync(
        string apiKey,
        string phone,
        decimal cod,
        bool feeInAdvance = false,
        string area = "Mirpur 10",
        string line1 = "House 9, Road 6")
    {
        var order = new
        {
            Customer = new { Name = "Advance Customer", Phone = phone },
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

    private static async Task PostAsync(HttpClient client, string page, string handler, params (string Name, string Value)[] fields)
    {
        var token = Token().Match(await client.GetStringAsync(page, Cancel)).Groups[1].Value;
        var form = new FormUrlEncodedContent(
        [
            .. fields.Select(field => KeyValuePair.Create(field.Name, field.Value)),
            KeyValuePair.Create("__RequestVerificationToken", token)
        ]);
        var response = await client.PostAsync($"{page}&handler={handler}", form, Cancel);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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
        return "017" + Random.Shared.Next(0, 100_000_000).ToString("D8");
    }

    [GeneratedRegex("""name="__RequestVerificationToken" type="hidden" value="([^"]+)""")]
    private static partial Regex Token();

    private sealed record PlacedOrder(string Number, CustomerStep WaitsFor);

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
