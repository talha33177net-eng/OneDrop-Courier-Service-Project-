using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Application.Abstractions;
using Domain.Pricing;
using Infrastructure.MultiTenancy;
using Infrastructure.Persistence;

namespace Integration.Tests;

/// <summary>
/// Task 2.3: every order is priced for the group it joins, at its tenant's prices, and the merchant is told only
/// what its own order adds. The expected amounts are read from the tenant, never written here, except the Dhaka
/// demo total the plan names.
/// </summary>
public class PricingTests(WebAppFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [Fact]
    public async Task Three_shops_pay_the_base_fee_and_two_extra_fees_and_the_group_costs_110()
    {
        WebAppFactory.RequireDatabase();
        var dhaka = await TenantAsync("dhaka");
        var phone = NewPhone();

        var fashion = await CreateAsync(WebAppFactory.DhakaFashion, NewOrder(phone));
        var gadget = await CreateAsync(WebAppFactory.DhakaGadget, NewOrder(phone));
        var beauty = await CreateAsync(WebAppFactory.DhakaBeauty, NewOrder(phone));

        Assert.Equal(dhaka.BaseDeliveryFee, fashion.Fee);
        Assert.Equal(dhaka.ExtraShopFee, gadget.Fee);
        Assert.Equal(dhaka.ExtraShopFee, beauty.Fee);
        var groupFee = await GroupFeeAsync(fashion.Number);
        Assert.Equal(dhaka.BaseDeliveryFee + 2 * dhaka.ExtraShopFee, groupFee);
        Assert.Equal(110, groupFee);
    }

    [Fact]
    public async Task A_second_order_from_the_same_shop_adds_nothing()
    {
        WebAppFactory.RequireDatabase();
        var dhaka = await TenantAsync("dhaka");
        var phone = NewPhone();

        var first = await CreateAsync(WebAppFactory.DhakaFashion, NewOrder(phone));
        var second = await CreateAsync(WebAppFactory.DhakaFashion, NewOrder(phone));

        Assert.Equal(dhaka.BaseDeliveryFee, first.Fee);
        Assert.Equal(0, second.Fee);
        Assert.Equal(dhaka.BaseDeliveryFee, await GroupFeeAsync(first.Number));
    }

    [Fact]
    public async Task Deliver_fast_costs_the_fast_fee_and_Dont_hold_the_base_fee_beside_an_open_group()
    {
        WebAppFactory.RequireDatabase();
        var dhaka = await TenantAsync("dhaka");
        var phone = NewPhone();
        await CreateAsync(WebAppFactory.DhakaFashion, NewOrder(phone));

        var fast = await CreateAsync(WebAppFactory.DhakaGadget, NewOrder(phone) with { Speed = "fast" });
        var food = await CreateAsync(WebAppFactory.DhakaBeauty, NewOrder(phone) with { DoNotHold = true });

        Assert.Equal(dhaka.FastDeliveryFee, fast.Fee);
        Assert.Equal(dhaka.BaseDeliveryFee, food.Fee);
    }

    [Fact]
    public async Task Chattogram_charges_its_own_prices_for_the_same_phone()
    {
        WebAppFactory.RequireDatabase();
        var dhaka = await TenantAsync("dhaka");
        var chattogram = await TenantAsync("chattogram");
        var phone = NewPhone();

        var inDhaka = await CreateAsync(WebAppFactory.DhakaFashion, NewOrder(phone));
        var inChattogram = await CreateAsync(
            WebAppFactory.ChattogramFashion,
            NewOrder(phone) with { Address = new AddressBody("Agrabad", "House 4, Road 2") });

        Assert.NotEqual(dhaka.BaseDeliveryFee, chattogram.BaseDeliveryFee);
        Assert.Equal(dhaka.BaseDeliveryFee, inDhaka.Fee);
        Assert.Equal(chattogram.BaseDeliveryFee, inChattogram.Fee);
    }

    [Fact]
    public async Task A_replay_and_the_order_details_show_the_fee_the_order_was_given()
    {
        WebAppFactory.RequireDatabase();
        var dhaka = await TenantAsync("dhaka");
        var phone = NewPhone();
        await CreateAsync(WebAppFactory.DhakaFashion, NewOrder(phone));
        var client = factory.ClientFor(WebAppFactory.DhakaGadget);
        var key = Guid.NewGuid().ToString();

        var created = await PostAsync(client, NewOrder(phone), key);
        var replayed = await PostAsync(client, NewOrder(phone), key);
        var details = await client.GetFromJsonAsync<Priced>(
            $"/api/v1/orders/{created.Number}",
            Json,
            TestContext.Current.CancellationToken);

        Assert.Equal(dhaka.ExtraShopFee, created.Fee);
        Assert.Equal(created, replayed);
        Assert.Equal(created.Fee, details!.Fee);
    }

    private async Task<Priced> CreateAsync(string apiKey, OrderRequest order)
    {
        var response = await factory.ClientFor(apiKey).PostAsJsonAsync("/api/v1/orders", order, Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<Priced>(Json))!;
    }

    private static async Task<Priced> PostAsync(HttpClient client, OrderRequest order, string idempotencyKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders")
        {
            Content = JsonContent.Create(order, options: Json)
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        var response = await client.SendAsync(request);
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}");

        return (await response.Content.ReadFromJsonAsync<Priced>(Json))!;
    }

    /// <summary>The fee the customer would pay at the door for the group <paramref name="orderNumber"/> is in.</summary>
    private async Task<decimal> GroupFeeAsync(string orderNumber)
    {
        var tenant = await TenantAsync("dhaka");
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(tenant);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var groupId = await db.Orders
            .Where(o => o.Number == orderNumber)
            .Select(o => o.DeliveryGroupId)
            .SingleAsync(TestContext.Current.CancellationToken);
        var orders = await db.Orders
            .Where(o => o.DeliveryGroupId == groupId)
            .Select(o => new FeeLine(o.MerchantId, o.Speed, o.Status))
            .ToListAsync(TestContext.Current.CancellationToken);

        return new DeliveryFeeCalculator(tenant.Fees).GroupFee(orders);
    }

    private async Task<TenantInfo> TenantAsync(string slug)
    {
        return (await factory.Services.GetRequiredService<ITenantCatalog>().FindBySlugAsync(slug))!;
    }

    private static string NewPhone()
    {
        return "017" + Random.Shared.Next(0, 100_000_000).ToString("D8");
    }

    private static OrderRequest NewOrder(string phone)
    {
        return new OrderRequest(
            new CustomerBody("Pricing Customer", phone),
            new AddressBody("Mirpur 10", "House 9, Road 4"),
            [new PackageBody("Parcel", 400)],
            800);
    }

    private sealed record OrderRequest(CustomerBody Customer, AddressBody Address, PackageBody[] Packages, decimal CodAmount)
    {
        public string Speed { get; init; } = "combine";

        public bool DoNotHold { get; init; }
    }

    private sealed record CustomerBody(string Name, string Phone);

    private sealed record AddressBody(string Area, string Line1);

    private sealed record PackageBody(string Description, int WeightGrams);

    private sealed record Priced(string Number, decimal Fee);
}
