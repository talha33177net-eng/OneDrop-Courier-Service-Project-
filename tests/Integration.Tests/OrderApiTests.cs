using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Integration.Tests;

/// <summary>Week 1 "done when": an order can be created for a tenant - and only that tenant and merchant see it.</summary>
public class OrderApiTests(WebAppFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [Fact]
    public async Task A_merchant_creates_an_order_for_its_tenant()
    {
        WebAppFactory.RequireDatabase();
        var client = factory.ClientFor(WebAppFactory.DhakaFashion);

        var response = await client.PostAsJsonAsync("/api/v1/orders", NewOrder(NewPhone(), "Mirpur 10"), Json);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<Created>(Json);
        Assert.StartsWith("OD-", created!.Number);
        Assert.Equal("Mirpur", created.Zone);
        Assert.Equal("Mirpur hub", created.Hub);
        Assert.Equal(response.Headers.Location!.AbsolutePath, $"/api/v1/orders/{created.Number}");
    }

    [Fact]
    public async Task Another_merchant_and_another_tenant_get_404_for_the_order()
    {
        WebAppFactory.RequireDatabase();
        var created = await CreateAsync(WebAppFactory.DhakaFashion, NewOrder(NewPhone(), "Mirpur 10"));

        var own = await factory.ClientFor(WebAppFactory.DhakaFashion).GetAsync($"/api/v1/orders/{created.Number}");
        var otherMerchant = await factory.ClientFor(WebAppFactory.DhakaGadget).GetAsync($"/api/v1/orders/{created.Number}");
        var otherTenant = await factory.ClientFor(WebAppFactory.ChattogramFashion).GetAsync($"/api/v1/orders/{created.Number}");

        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, otherMerchant.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, otherTenant.StatusCode);
    }

    [Fact]
    public async Task The_same_phone_in_another_tenant_is_another_customer()
    {
        WebAppFactory.RequireDatabase();
        var phone = NewPhone();

        var dhaka = await CreateAsync(WebAppFactory.DhakaFashion, NewOrder(phone, "Mirpur 10"));
        var chattogram = await CreateAsync(WebAppFactory.ChattogramFashion, NewOrder(phone, "Agrabad"));

        Assert.NotEqual(dhaka.CustomerId, chattogram.CustomerId);
    }

    [Fact]
    public async Task A_retry_with_the_same_idempotency_key_returns_the_first_order()
    {
        WebAppFactory.RequireDatabase();
        var client = factory.ClientFor(WebAppFactory.DhakaFashion);
        var key = Guid.NewGuid().ToString();
        var order = NewOrder(NewPhone(), "Mirpur 10");

        var first = await PostAsync(client, order, key);
        var retry = await PostAsync(client, order, key);
        var changed = await PostAsync(client, order with { CodAmount = order.CodAmount + 1 }, key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(
            (await first.Content.ReadFromJsonAsync<Created>(Json))!.Number,
            (await retry.Content.ReadFromJsonAsync<Created>(Json))!.Number);
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
    }

    [Fact]
    public async Task Shops_sending_orders_for_a_new_customer_at_the_same_moment_find_one_customer()
    {
        WebAppFactory.RequireDatabase();
        var phone = NewPhone();
        string[] keys = [WebAppFactory.DhakaFashion, WebAppFactory.DhakaGadget, WebAppFactory.DhakaBeauty];

        var orders = await Task.WhenAll(keys
            .SelectMany(key => Enumerable.Repeat(key, 2))
            .Select(key => CreateAsync(key, NewOrder(phone, "Mirpur 10"))));

        Assert.Single(orders.Select(o => o.CustomerId).Distinct());
    }

    [Fact]
    public async Task An_invalid_order_is_rejected_field_by_field()
    {
        WebAppFactory.RequireDatabase();
        var client = factory.ClientFor(WebAppFactory.DhakaFashion);

        var response = await client.PostAsJsonAsync(
            "/api/v1/orders",
            NewOrder("12345", "Mirpur 10") with { Packages = [] },
            Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Customer.Phone", body);
        Assert.Contains("Packages", body);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("od_dhkfashion01_WrongSecretWrongSecretWrongSecre1")]
    public async Task Without_a_valid_key_the_api_answers_401(string? apiKey)
    {
        WebAppFactory.RequireDatabase();
        var client = apiKey is null ? factory.CreateClient() : factory.ClientFor(apiKey);

        var response = await client.GetAsync("/api/v1/areas");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<Created> CreateAsync(string apiKey, OrderRequest order)
    {
        var response = await factory.ClientFor(apiKey).PostAsJsonAsync("/api/v1/orders", order, Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<Created>(Json))!;
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, OrderRequest order, string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders") { Content = JsonContent.Create(order, options: Json) };
        request.Headers.Add("Idempotency-Key", idempotencyKey);

        return client.SendAsync(request);
    }

    private static string NewPhone()
    {
        return "017" + Random.Shared.Next(0, 100_000_000).ToString("D8");
    }

    private static OrderRequest NewOrder(string phone, string area)
    {
        return new OrderRequest(
            new CustomerBody("Test Customer", phone),
            new AddressBody(area, "House 12, Road 5"),
            [new PackageBody("T-shirt", 400)],
            800);
    }

    private sealed record OrderRequest(CustomerBody Customer, AddressBody Address, PackageBody[] Packages, decimal CodAmount);

    private sealed record CustomerBody(string Name, string Phone);

    private sealed record AddressBody(string Area, string Line1);

    private sealed record PackageBody(string Description, int WeightGrams);

    private sealed record Created(long OrderId, string Number, long CustomerId, string Zone, string Hub);
}
