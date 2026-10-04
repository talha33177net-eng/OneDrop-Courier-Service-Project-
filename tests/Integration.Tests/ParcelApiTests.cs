using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Integration.Tests;

/// <summary>The merchant API: booking with idempotency and validation, reading one's own parcel, charges, cancelling.</summary>
public class ParcelApiTests(WebAppFactory factory) : AppTests(factory)
{
    private static object Body(string? phone = "01811000101", string area = "Gulshan 2", decimal cod = 1250, decimal weightKg = 1.5m)
    {
        return new
        {
            MerchantReference = "INV-1001",
            RecipientName = "Rahim Uddin",
            RecipientPhone = phone,
            RecipientAddress = "House 22, Road 4",
            Area = area,
            CodAmount = cod,
            WeightKg = weightKg,
            ItemDescription = "Panjabi"
        };
    }

    [Fact]
    public async Task A_parcel_is_booked_priced_from_the_rate_card_and_a_retry_with_the_same_key_returns_it()
    {
        WebAppFactory.RequireDatabase();
        var shop = await NewMerchantAsync(area: "Mirpur 10");
        var client = Factory.ClientFor(shop.ApiKey);
        var key = Guid.NewGuid().ToString("N");

        var first = await PostAsync(client, Body(), key);
        var again = await PostAsync(client, Body(), key);
        var otherBody = await PostAsync(client, Body(cod: 99), key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var booked = await first.Content.ReadFromJsonAsync<JsonElement>(Json, Cancel);
        var code = booked.GetProperty("trackingCode").GetString()!;
        Assert.Matches("^OD[0-9]{8}$", code);
        Assert.Equal("pending", booked.GetProperty("status").GetString());
        Assert.Equal("insideCity", booked.GetProperty("serviceArea").GetString());
        Assert.Equal(75, booked.GetProperty("deliveryCharge").GetDecimal());
        Assert.Equal(13, booked.GetProperty("codCharge").GetDecimal());
        Assert.Equal(88, booked.GetProperty("totalCharge").GetDecimal());
        Assert.Equal("Gulshan hub", booked.GetProperty("deliveryHub").GetString());

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(code, (await again.Content.ReadFromJsonAsync<JsonElement>(Json, Cancel)).GetProperty("trackingCode").GetString());
        Assert.Equal(HttpStatusCode.Conflict, otherBody.StatusCode);
    }

    [Fact]
    public async Task Each_service_area_has_its_own_price()
    {
        WebAppFactory.RequireDatabase();
        var shop = await NewMerchantAsync(area: "Mirpur 10");
        var client = Factory.ClientFor(shop.ApiKey);

        async Task<decimal> ChargeAsync(string area)
        {
            var response = await client.GetAsync($"/api/v1/charge?area={Uri.EscapeDataString(area)}&weightKg=0.5&codAmount=1000", Cancel);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            return (await response.Content.ReadFromJsonAsync<JsonElement>(Json, Cancel)).GetProperty("deliveryCharge").GetDecimal();
        }

        Assert.Equal(60, await ChargeAsync("Dhanmondi"));
        Assert.Equal(100, await ChargeAsync("Savar"));
        Assert.Equal(120, await ChargeAsync("Sylhet Sadar"));
    }

    [Fact]
    public async Task A_bad_request_names_each_field_and_books_nothing()
    {
        WebAppFactory.RequireDatabase();
        var shop = await NewMerchantAsync();
        var client = Factory.ClientFor(shop.ApiKey);

        var bad = await client.PostAsJsonAsync("/api/v1/parcels", new { RecipientPhone = "123", CodAmount = -5, WeightKg = 0 }, Cancel);
        var unknownArea = await PostAsync(client, Body(area: "Atlantis"));

        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var errors = (await bad.Content.ReadFromJsonAsync<JsonElement>(Json, Cancel)).GetProperty("errors");
        foreach (var field in new[] { "RecipientName", "RecipientPhone", "RecipientAddress", "Area", "CodAmount", "WeightKg" })
        {
            Assert.True(errors.TryGetProperty(field, out _), $"No error for {field}");
        }

        Assert.Equal(HttpStatusCode.BadRequest, unknownArea.StatusCode);
        Assert.Contains("parcel.area.unknown", await unknownArea.Content.ReadAsStringAsync(Cancel));
        Assert.Equal(0, await QueryAsync("onedrop", db => db.Parcels.CountAsync(p => p.MerchantId == shop.Id, Cancel)));
    }

    [Fact]
    public async Task A_merchant_reads_only_its_own_parcels_and_another_courier_none()
    {
        WebAppFactory.RequireDatabase();
        var shop = await NewMerchantAsync();
        var other = await NewMerchantAsync();
        var code = await BookAsync(shop.ApiKey);

        var own = await Factory.ClientFor(shop.ApiKey).GetAsync($"/api/v1/parcels/{code}", Cancel);
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Equal("pending", (await own.Content.ReadFromJsonAsync<JsonElement>(Json, Cancel)).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await Factory.ClientFor(other.ApiKey).GetAsync($"/api/v1/parcels/{code}", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Factory.ClientFor(WebAppFactory.Rival).GetAsync($"/api/v1/parcels/{code}", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Factory.CreateClient().GetAsync($"/api/v1/parcels/{code}", Cancel)).StatusCode);
    }

    [Fact]
    public async Task Another_courier_cannot_book_to_this_couriers_area()
    {
        WebAppFactory.RequireDatabase();

        var response = await PostAsync(Factory.ClientFor(WebAppFactory.Rival), Body(area: "Gulshan 2"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("parcel.area.unknown", await response.Content.ReadAsStringAsync(Cancel));
    }

    [Fact]
    public async Task A_parcel_is_cancelled_before_pickup_only_and_never_by_another_merchant()
    {
        WebAppFactory.RequireDatabase();
        var shop = await NewMerchantAsync();
        var other = await NewMerchantAsync();
        var code = await BookAsync(shop.ApiKey);

        Assert.Equal(HttpStatusCode.NotFound, (await Factory.ClientFor(other.ApiKey).PostAsJsonAsync($"/api/v1/parcels/{code}/cancel", new { }, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Factory.ClientFor(shop.ApiKey).PostAsJsonAsync($"/api/v1/parcels/{code}/cancel", new { Reason = "Customer changed their mind" }, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Factory.ClientFor(shop.ApiKey).PostAsJsonAsync($"/api/v1/parcels/{code}/cancel", new { }, Cancel)).StatusCode);
        Assert.Equal(Domain.Parcels.ParcelStatus.Cancelled, (await ParcelAsync(code)).Status);
    }

    [Fact]
    public async Task A_merchant_waiting_for_approval_cannot_book()
    {
        WebAppFactory.RequireDatabase();
        var shop = await NewMerchantAsync(approved: false);

        var response = await PostAsync(Factory.ClientFor(shop.ApiKey), Body());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("parcel.merchantNotActive", await response.Content.ReadAsStringAsync(Cancel));
    }

    [Fact]
    public async Task The_area_list_is_the_couriers_own()
    {
        WebAppFactory.RequireDatabase();

        var ours = await Factory.ClientFor(WebAppFactory.Fashion).GetStringAsync("/api/v1/areas", Cancel);
        var theirs = await Factory.ClientFor(WebAppFactory.Rival).GetStringAsync("/api/v1/areas", Cancel);

        Assert.Contains("Sylhet Sadar", ours);
        Assert.DoesNotContain("Rival Town", ours);
        Assert.Contains("Rival Town", theirs);
        Assert.DoesNotContain("Gulshan", theirs);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, object body, string? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/parcels") { Content = JsonContent.Create(body) };
        if (key is not null)
        {
            request.Headers.Add("Idempotency-Key", key);
        }

        return await client.SendAsync(request, Cancel);
    }
}
