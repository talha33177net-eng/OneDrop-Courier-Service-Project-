using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Application.Abstractions;
using Domain.Customers;
using Infrastructure.MultiTenancy;
using Infrastructure.Persistence;

namespace Integration.Tests;

/// <summary>
/// Task 2.4: the checkout quote is the fee Create Order then gives the same order, and it tells the merchant nothing
/// about the other shops. Amounts are read from the tenant.
/// </summary>
public class QuoteTests(WebAppFactory factory)
{
    private const string Area = "Mirpur 10";
    private const string Home = "House 12, Road 5";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [Fact]
    public async Task Each_shops_quote_is_the_fee_its_order_is_then_given()
    {
        WebAppFactory.RequireDatabase();
        var dhaka = await TenantAsync("dhaka");
        var phone = NewPhone();
        var quotes = new List<Quote>();
        var fees = new List<decimal>();

        foreach (var shop in new[] { WebAppFactory.DhakaFashion, WebAppFactory.DhakaGadget, WebAppFactory.DhakaBeauty })
        {
            quotes.Add(await QuoteAsync(shop, $"phone={phone}&area={Area}&line1={Home}"));
            fees.Add(await CreateAsync(shop, phone, Home));
        }

        Assert.Equal(fees, quotes.Select(quote => quote.Fee));
        Assert.Equal([dhaka.BaseDeliveryFee, dhaka.ExtraShopFee, dhaka.ExtraShopFee], fees);
        Assert.Equal([false, true, true], quotes.Select(quote => quote.JoinsDelivery));
        Assert.All(quotes, quote => Assert.Equal(dhaka.CurrencyCode, quote.Currency));
    }

    [Fact]
    public async Task A_shop_already_in_the_delivery_is_quoted_nothing_and_the_address_spelling_does_not_matter()
    {
        WebAppFactory.RequireDatabase();
        var phone = NewPhone();
        await CreateAsync(WebAppFactory.DhakaFashion, phone, Home);

        var quote = await QuoteAsync(
            WebAppFactory.DhakaFashion,
            $"phone=%2B88{phone}&area={Area}&line1=house 12 road 5");

        Assert.Equal(new Quote(0, (await TenantAsync("dhaka")).CurrencyCode, true), quote);
    }

    [Fact]
    public async Task Fast_Dont_hold_and_another_address_are_quoted_a_delivery_of_their_own()
    {
        WebAppFactory.RequireDatabase();
        var dhaka = await TenantAsync("dhaka");
        var phone = NewPhone();
        await CreateAsync(WebAppFactory.DhakaFashion, phone, Home);

        var fast = await QuoteAsync(WebAppFactory.DhakaGadget, $"phone={phone}&area={Area}&line1={Home}&speed=fast");
        var food = await QuoteAsync(WebAppFactory.DhakaGadget, $"phone={phone}&area={Area}&line1={Home}&doNotHold=true");
        var office = await QuoteAsync(WebAppFactory.DhakaGadget, $"phone={phone}&area={Area}&line1=Office 3, Road 11");

        Assert.Equal(new Quote(dhaka.FastDeliveryFee, dhaka.CurrencyCode, false), fast);
        Assert.Equal(new Quote(dhaka.BaseDeliveryFee, dhaka.CurrencyCode, false), food);
        Assert.Equal(new Quote(dhaka.BaseDeliveryFee, dhaka.CurrencyCode, false), office);
    }

    [Fact]
    public async Task Another_tenant_quotes_its_own_prices_and_ignores_the_delivery_elsewhere()
    {
        WebAppFactory.RequireDatabase();
        var chattogram = await TenantAsync("chattogram");
        var phone = NewPhone();
        await CreateAsync(WebAppFactory.DhakaFashion, phone, Home);

        var quote = await QuoteAsync(WebAppFactory.ChattogramFashion, $"phone={phone}&area=Agrabad&line1={Home}");

        Assert.Equal(new Quote(chattogram.BaseDeliveryFee, chattogram.CurrencyCode, false), quote);
    }

    [Fact]
    public async Task A_quote_for_a_new_customer_creates_nothing()
    {
        WebAppFactory.RequireDatabase();
        var phone = NewPhone();

        await QuoteAsync(WebAppFactory.DhakaFashion, $"phone={phone}&area={Area}&line1={Home}");

        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(await TenantAsync("dhaka"));
        var e164 = PhoneNumber.Parse(phone).Value.Value;
        Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Customers
            .AnyAsync(c => c.Phone == e164, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("phone=12345&area=Mirpur 10&line1=House 1", "Phone")]
    [InlineData("phone=01712345678&line1=House 1", "Area")]
    [InlineData("phone=01712345678&area=Mirpur 10", "Line1")]
    public async Task A_missing_or_invalid_field_is_a_400_naming_it(string query, string field)
    {
        WebAppFactory.RequireDatabase();

        var response = await factory.ClientFor(WebAppFactory.DhakaFashion).GetAsync(
            $"/api/v1/quote?{query}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains($"\"{field}\"", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_area_of_another_tenant_is_unknown()
    {
        WebAppFactory.RequireDatabase();

        var response = await factory.ClientFor(WebAppFactory.DhakaFashion).GetAsync(
            $"/api/v1/quote?phone={NewPhone()}&area=Agrabad&line1={Home}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("quote.area.unknown", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_quote_needs_an_api_key()
    {
        WebAppFactory.RequireDatabase();

        var response = await factory.CreateClient().GetAsync(
            $"/api/v1/quote?phone={NewPhone()}&area={Area}&line1={Home}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<Quote> QuoteAsync(string apiKey, string query)
    {
        var response = await factory.ClientFor(apiKey).GetAsync(
            $"/api/v1/quote?{query}",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<Quote>(Json, TestContext.Current.CancellationToken))!;
    }

    private async Task<decimal> CreateAsync(string apiKey, string phone, string line1)
    {
        var order = new
        {
            Customer = new { Name = "Quote Customer", Phone = phone },
            Address = new { Area, Line1 = line1 },
            Packages = new[] { new { Description = "Parcel", WeightGrams = 400 } },
            CodAmount = 800
        };
        var response = await factory.ClientFor(apiKey).PostAsJsonAsync(
            "/api/v1/orders",
            order,
            Json,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<Priced>(Json, TestContext.Current.CancellationToken))!.Fee;
    }

    private async Task<TenantInfo> TenantAsync(string slug)
    {
        return (await factory.Services.GetRequiredService<ITenantCatalog>().FindBySlugAsync(slug))!;
    }

    private static string NewPhone()
    {
        return "018" + Random.Shared.Next(0, 100_000_000).ToString("D8");
    }

    private sealed record Quote(decimal Fee, string Currency, bool JoinsDelivery);

    private sealed record Priced(decimal Fee);
}
