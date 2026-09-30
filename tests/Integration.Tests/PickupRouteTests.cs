using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Application.Abstractions;
using Application.Network.PickupRoutes;
using Domain.Merchants;
using Domain.Orders;
using Infrastructure.MultiTenancy;
using Infrastructure.Persistence;

namespace Integration.Tests;

/// <summary>
/// Task 3.1: pickup routes and parcel labels. The demo shops are all in Mirpur and every test class orders from
/// them, so these tests open shops of their own in Uttara and check who is on a sheet, not Mirpur's totals. The hub
/// scan tests also open an Uttara shop, so the two classes share a collection and never run at the same time: the
/// route list and a sheet read a moment apart would otherwise count another class's new parcels differently.
/// </summary>
[Collection("Uttara pickups")]
public partial class PickupRouteTests(WebAppFactory factory)
{
    private const string Password = "OneDrop#2026";

    [Fact]
    public async Task A_route_visits_only_its_zones_pickup_points_with_parcels_waiting()
    {
        WebAppFactory.RequireDatabase();
        var uttaraShop = await NewShopAsync("dhaka", "Uttara Sector 7");
        var collectedShop = await NewShopAsync("dhaka", "Uttara Sector 10");
        var waiting = await CreateAsync(uttaraShop.ApiKey, packages: 2, speed: "fast");
        var collected = await CreateAsync(collectedShop.ApiKey);
        var mirpur = await CreateAsync(WebAppFactory.DhakaFashion);
        await MoveAsync("dhaka", collected, OrderStatus.PickedUp);

        var routes = await RoutesAsync("dhaka", handler => handler.ListAsync(Cancel));
        var uttaraRoute = routes.Single(route => route.Zone == "Uttara");
        var mirpurRoute = routes.Single(route => route.Zone == "Mirpur");
        var uttara = (await RoutesAsync("dhaka", handler => handler.GetAsync(uttaraRoute.Id, Cancel)))!;
        var mirpurSheet = (await RoutesAsync("dhaka", handler => handler.GetAsync(mirpurRoute.Id, Cancel)))!;

        var stop = uttara.Stops.Single(s => s.Merchant == uttaraShop.Name);
        Assert.Equal($"{uttaraShop.Name}, Uttara Sector 7", stop.Address);
        var order = Assert.Single(stop.Orders);
        Assert.Equal((waiting, 2, true), (order.Number, order.Packages, order.Urgent));
        Assert.Equal([$"{waiting}-1", $"{waiting}-2"], order.Labels.Select(label => label.ToString()));
        Assert.DoesNotContain(uttara.Stops, s => s.Merchant == collectedShop.Name);
        Assert.DoesNotContain(uttara.Stops, s => s.Merchant == "Fashion House");
        Assert.Equal("Uttara hub", uttara.Hub);

        Assert.Contains(mirpurSheet.Stops.Single(s => s.Merchant == "Fashion House").Orders, o => o.Number == mirpur);
        Assert.DoesNotContain(mirpurSheet.Stops, s => s.Merchant == uttaraShop.Name);

        // The list counts the same parcels the sheet says to collect: an order still waiting for its delivery fee in
        // advance (task 3.6b) is listed on the sheet but stays at the shop, so neither counts it
        Assert.Equal(
            (
                uttara.Stops.Count(s => s.Orders.Any(o => o.Collect)),
                uttara.Stops.Sum(s => s.Orders.Count(o => o.Collect)),
                uttara.Packages
            ),
            (uttaraRoute.Stops, uttaraRoute.Orders, uttaraRoute.Packages));
    }

    [Fact]
    public async Task Every_zone_has_its_operators_route_and_no_other_operators()
    {
        WebAppFactory.RequireDatabase();

        // Leaving out the zones the trip tests build for themselves (TripTests.NewHubAsync)
        var dhaka = (await RoutesAsync("dhaka", handler => handler.ListAsync(Cancel)))
            .Where(route => !route.Zone.StartsWith("Trip test", StringComparison.Ordinal))
            .ToList();
        var chattogram = await RoutesAsync("chattogram", handler => handler.ListAsync(Cancel));
        var dhakaRouteFromChattogram = await RoutesAsync("chattogram", handler => handler.GetAsync(dhaka[0].Id, Cancel));

        Assert.Equal(
            ["Banani", "Dhanmondi", "Gulshan", "Mirpur", "Mohammadpur", "Motijheel", "Uttara"],
            dhaka.Select(route => route.Zone));
        Assert.Equal(
            ["Agrabad", "Chawkbazar", "Halishahar", "Nasirabad", "Panchlaish"],
            chattogram.Select(route => route.Zone));
        // Staggered by DbUp 005, farthest zones first, every parcel at its hub by about 14:00
        Assert.Equal(
            ["13:00", "13:00", "13:30", "11:30", "12:30", "12:00", "11:00"],
            dhaka.Select(route => route.PickupTime.ToString("HH:mm", CultureInfo.InvariantCulture)));
        Assert.Equal(
            ["12:30", "12:00", "11:00", "11:30", "13:00"],
            chattogram.Select(route => route.PickupTime.ToString("HH:mm", CultureInfo.InvariantCulture)));
        Assert.Null(dhakaRouteFromChattogram);
    }

    [Fact]
    public async Task Hub_staff_open_their_operators_route_sheets_and_nobody_else_can()
    {
        WebAppFactory.RequireDatabase();
        var shop = await NewShopAsync("dhaka", "Uttara Sector 4");
        var order = await CreateAsync(shop.ApiKey);
        var routeId = (await RoutesAsync("dhaka", handler => handler.ListAsync(Cancel))).Single(r => r.Zone == "Uttara").Id;
        var hub = await StaffSignInAsync("dhaka", "hub@dhaka.onedrop.test");
        var admin = await StaffSignInAsync("dhaka", "admin@dhaka.onedrop.test");
        var merchant = await StaffSignInAsync("dhaka", "fashion@dhaka.onedrop.test");
        var chattogramHub = await StaffSignInAsync("chattogram", "hub@chattogram.onedrop.test");
        var anonymous = factory.CreateClient(Options("dhaka"));

        var list = await hub.GetStringAsync("/Hub/Routes", Cancel);
        var sheet = await hub.GetStringAsync($"/Hub/RouteSheet/{routeId}", Cancel);

        Assert.Contains("Uttara hub", list);
        Assert.Contains($"/Hub/RouteSheet/{routeId}", list);
        Assert.Contains(shop.Name, sheet);
        Assert.Contains($"{order}-1", sheet);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/Hub/RouteSheet/{routeId}", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await chattogramHub.GetAsync($"/Hub/RouteSheet/{routeId}", Cancel)).StatusCode);
        var forMerchant = await merchant.GetAsync("/Hub/Routes", Cancel);
        Assert.Equal(HttpStatusCode.Redirect, forMerchant.StatusCode);
        Assert.StartsWith("/Account/AccessDenied", forMerchant.Headers.Location!.PathAndQuery);
        var forAnonymous = await anonymous.GetAsync($"/Hub/RouteSheet/{routeId}", Cancel);
        Assert.StartsWith("/Account/Login", forAnonymous.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task A_merchant_prints_a_qr_label_per_parcel_of_its_own_orders_only()
    {
        WebAppFactory.RequireDatabase();
        var phone = NewPhone();
        var mine = await CreateAsync(WebAppFactory.DhakaFashion, packages: 2, cod: 1500, phone: phone);
        var theirs = await CreateAsync(WebAppFactory.DhakaGadget, phone: phone);
        var fashion = await StaffSignInAsync("dhaka", "fashion@dhaka.onedrop.test");

        var labels = await fashion.GetStringAsync($"/Merchant/Labels?order={mine}", Cancel);
        var waiting = await fashion.GetStringAsync("/Merchant/Labels", Cancel);
        var other = await fashion.GetStringAsync($"/Merchant/Labels?order={theirs}", Cancel);
        var both = await fashion.GetStringAsync($"/Merchant/Labels?order={mine}&order={theirs}", Cancel);

        Assert.Equal(2, LabelCount().Count(labels));
        Assert.Equal(2, QrCode().Count(labels));
        Assert.Contains($"{mine}-1", labels);
        Assert.Contains($"{mine}-2", labels);
        Assert.Contains("Package 2 of 2", labels);
        Assert.Contains("MIR", labels);
        Assert.Contains("COD ৳1,500", labels);
        Assert.DoesNotContain("DG-", labels);
        Assert.DoesNotContain("Gadget BD", labels);
        Assert.Contains($"{mine}-2", waiting);
        Assert.Contains("No parcels to label", other);
        Assert.DoesNotContain(theirs, other);
        Assert.DoesNotContain(theirs, both);
        Assert.Equal(2, LabelCount().Count(both));
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private async Task<T> RoutesAsync<T>(string slug, Func<PickupRoutesHandler, Task<T>> query)
    {
        await using var scope = await ScopeAsync(slug);

        return await query(scope.ServiceProvider.GetRequiredService<PickupRoutesHandler>());
    }

    /// <summary>A shop of this test's own, with its default pickup point in <paramref name="area"/>.</summary>
    private async Task<(string Name, string ApiKey)> NewShopAsync(string slug, string area)
    {
        await using var scope = await ScopeAsync(slug);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var name = $"Route Shop {Guid.NewGuid():N}"[..20];
        var pickupArea = await db.Areas.SingleAsync(a => a.Name == area, Cancel);
        var merchant = new Merchant(name, pickupArea.ZoneId, "01711999999", null);
        db.Merchants.Add(merchant);
        await db.SaveChangesAsync(Cancel);

        var (key, plaintext) = MerchantApiKey.Issue(merchant.Id, "Route test");
        db.PickupPoints.Add(new PickupPoint(merchant.Id, pickupArea.Id, "Shop", $"{name}, {area}", "01711999999", isDefault: true));
        db.MerchantApiKeys.Add(key);
        await db.SaveChangesAsync(Cancel);

        return (name, plaintext);
    }

    private async Task<string> CreateAsync(
        string apiKey,
        int packages = 1,
        decimal cod = 0,
        string speed = "combine",
        string? phone = null)
    {
        var order = new
        {
            Customer = new { Name = "Route Customer", Phone = phone ?? NewPhone() },
            Address = new { Area = "Mirpur 10", Line1 = "House 3, Road 8" },
            Packages = Enumerable.Range(1, packages).Select(_ => new { Description = "Parcel", WeightGrams = 400 }),
            CodAmount = cod,
            Speed = speed
        };
        var response = await factory.ClientFor(apiKey).PostAsJsonAsync("/api/v1/orders", order, Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<Created>(Cancel))!.Number;
    }

    private async Task MoveAsync(string slug, string number, OrderStatus status)
    {
        await using var scope = await ScopeAsync(slug);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var order = await db.Orders.SingleAsync(o => o.Number == number, Cancel);
        Assert.True(order.MoveTo(status).IsSuccess);
        await db.SaveChangesAsync(Cancel);
    }

    /// <summary>Signs a demo staff user in on their operator's subdomain. Keeps the sign-in cookie.</summary>
    private async Task<HttpClient> StaffSignInAsync(string slug, string email)
    {
        var client = factory.CreateClient(Options(slug));
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
        var tenant = await factory.Services.GetRequiredService<ITenantCatalog>().FindBySlugAsync(slug, Cancel);
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(tenant!);

        return scope;
    }

    private static string NewPhone()
    {
        return "019" + Random.Shared.Next(0, 100_000_000).ToString("D8");
    }

    [GeneratedRegex("""name="__RequestVerificationToken" type="hidden" value="([^"]+)""")]
    private static partial Regex Token();

    [GeneratedRegex("""<article class="label">""")]
    private static partial Regex LabelCount();

    [GeneratedRegex("<svg")]
    private static partial Regex QrCode();

    private sealed record Created(string Number);
}
