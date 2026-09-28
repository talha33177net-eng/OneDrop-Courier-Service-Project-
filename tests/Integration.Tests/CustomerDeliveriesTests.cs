using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Application.Abstractions;
using Application.Grouping.CustomerDeliveries;
using Domain.Grouping;
using Domain.Orders;
using Infrastructure.MultiTenancy;
using Infrastructure.Persistence;

namespace Integration.Tests;

/// <summary>
/// Task 2.8: "My deliveries" as the customer sees it, through the query behind the page (the page itself is
/// covered with a real sign-in in <see cref="ShipNowTests"/>, which has the phone login's rate limit to itself).
/// Every amount is read from the tenant.
/// </summary>
public class CustomerDeliveriesTests(WebAppFactory factory)
{
    private const string Area = "Mirpur 10";

    [Fact]
    public async Task Three_shops_show_as_one_delivery_with_their_packages_the_fee_so_far_and_the_saving()
    {
        WebAppFactory.RequireDatabase();
        var dhaka = await TenantAsync("dhaka");
        var phone = NewPhone();
        var fashion = await CreateAsync(WebAppFactory.DhakaFashion, phone, packages: 2, cod: 500);
        var gadget = await CreateAsync(WebAppFactory.DhakaGadget, phone, cod: 1200);
        var beauty = await CreateAsync(WebAppFactory.DhakaBeauty, phone);

        var deliveries = await DeliveriesAsync("dhaka", fashion.CustomerId);

        var delivery = Assert.Single(deliveries.OnTheWay);
        var group = await GroupAsync("dhaka", delivery.Number);
        var fee = dhaka.BaseDeliveryFee + 2 * dhaka.ExtraShopFee;
        Assert.Equal(DeliveryGroupStatus.Open, delivery.Status);
        Assert.Equal($"House 7, Road 2, {Area}", delivery.Address);
        Assert.Equal(["Beauty Shop", "Fashion House", "Gadget BD"], delivery.Shops);
        Assert.Equal([fashion.Number, gadget.Number, beauty.Number], delivery.Orders.Select(order => order.Number));
        Assert.Equal((4, 0), (delivery.Packages, delivery.PackagesCollected));
        Assert.Equal(fee, delivery.Fee);
        Assert.Equal(3 * dhaka.BaseDeliveryFee - fee, delivery.Savings);
        Assert.Equal(1700, delivery.Cod);
        Assert.Equal(LocalDate(dhaka, group.LocksAt), delivery.DeliveryDay);
        Assert.Equal(delivery.DeliveryDay.AddDays(-1), delivery.LastDayToJoin);
        Assert.Equal(dhaka.ExtraShopFee, deliveries.ExtraShopFee);
        Assert.Empty(deliveries.Earlier);
    }

    [Fact]
    public async Task Collected_packages_the_fee_and_the_cod_follow_what_happens_to_each_order()
    {
        WebAppFactory.RequireDatabase();
        var dhaka = await TenantAsync("dhaka");
        var phone = NewPhone();
        var fashion = await CreateAsync(WebAppFactory.DhakaFashion, phone, packages: 2, cod: 500);
        await CreateAsync(WebAppFactory.DhakaGadget, phone, cod: 1200);
        var beauty = await CreateAsync(WebAppFactory.DhakaBeauty, phone, cod: 300);

        await MoveAsync("dhaka", fashion.Number, OrderStatus.PickedUp);
        await MoveAsync("dhaka", beauty.Number, OrderStatus.Cancelled);
        var delivery = Assert.Single((await DeliveriesAsync("dhaka", fashion.CustomerId)).OnTheWay);

        var fee = dhaka.BaseDeliveryFee + dhaka.ExtraShopFee;
        Assert.Equal(["Fashion House", "Gadget BD"], delivery.Shops);
        Assert.Equal((3, 2), (delivery.Packages, delivery.PackagesCollected));
        Assert.Equal(fee, delivery.Fee);
        Assert.Equal(2 * dhaka.BaseDeliveryFee - fee, delivery.Savings);
        Assert.Equal(1700, delivery.Cod);

        // The cancelled order stays in the list, marked as such, so the customer sees why the fee dropped
        Assert.Equal(OrderStatus.Cancelled, delivery.Orders.Single(order => order.Number == beauty.Number).Status);
    }

    [Fact]
    public async Task A_fast_order_travels_alone_arrives_first_and_saves_nothing()
    {
        WebAppFactory.RequireDatabase();
        var dhaka = await TenantAsync("dhaka");
        var phone = NewPhone();
        var waiting = await CreateAsync(WebAppFactory.DhakaFashion, phone);
        var fast = await CreateAsync(WebAppFactory.DhakaGadget, phone, speed: "fast");

        var deliveries = await DeliveriesAsync("dhaka", waiting.CustomerId);

        Assert.Equal(2, deliveries.OnTheWay.Count);
        var alone = deliveries.OnTheWay[0];
        var tomorrow = LocalDate(dhaka, DateTime.UtcNow).AddDays(1);
        Assert.Equal([fast.Number], alone.Orders.Select(order => order.Number));
        Assert.Equal(DeliveryGroupStatus.Locked, alone.Status);
        Assert.Equal(tomorrow, alone.DeliveryDay);
        Assert.Equal((dhaka.FastDeliveryFee, 0m), (alone.Fee, alone.Savings));
        Assert.Equal([waiting.Number], deliveries.OnTheWay[1].Orders.Select(order => order.Number));
        Assert.Equal(DeliveryGroupStatus.Open, deliveries.OnTheWay[1].Status);
    }

    [Fact]
    public async Task A_delivery_out_with_a_rider_is_still_on_its_way_and_a_delivered_one_moves_to_earlier()
    {
        WebAppFactory.RequireDatabase();
        var dhaka = await TenantAsync("dhaka");
        var phone = NewPhone();
        var order = await CreateAsync(WebAppFactory.DhakaFashion, phone);
        await CreateAsync(WebAppFactory.DhakaBeauty, phone);
        var number = Assert.Single((await DeliveriesAsync("dhaka", order.CustomerId)).OnTheWay).Number;

        await ChangeGroupAsync("dhaka", number, group =>
        {
            group.ShipNow(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(dhaka.TimeZone));
            group.MoveTo(DeliveryGroupStatus.Dispatched, DateTime.UtcNow);
        });
        var dispatched = await DeliveriesAsync("dhaka", order.CustomerId);
        await ChangeGroupAsync("dhaka", number, group => group.MoveTo(DeliveryGroupStatus.Delivered, DateTime.UtcNow));
        var delivered = await DeliveriesAsync("dhaka", order.CustomerId);

        Assert.Equal(DeliveryGroupStatus.Dispatched, Assert.Single(dispatched.OnTheWay).Status);
        Assert.Empty(dispatched.Earlier);
        Assert.Empty(delivered.OnTheWay);
        var earlier = Assert.Single(delivered.Earlier);
        Assert.Equal((number, DeliveryGroupStatus.Delivered), (earlier.Number, earlier.Status));
        Assert.Equal(dhaka.BaseDeliveryFee + dhaka.ExtraShopFee, earlier.Fee);
    }

    [Fact]
    public async Task A_customer_sees_only_their_own_deliveries_with_this_operator_at_its_prices()
    {
        WebAppFactory.RequireDatabase();
        var chattogram = await TenantAsync("chattogram");
        var phone = NewPhone();
        var mine = await CreateAsync(WebAppFactory.DhakaFashion, phone);
        await CreateAsync(WebAppFactory.DhakaGadget, NewPhone());
        var elsewhere = await CreateAsync(WebAppFactory.ChattogramFashion, phone, area: "Agrabad");

        var inDhaka = await DeliveriesAsync("dhaka", mine.CustomerId);
        var inChattogram = await DeliveriesAsync("chattogram", elsewhere.CustomerId);
        var acrossOperators = await DeliveriesAsync("dhaka", elsewhere.CustomerId);

        Assert.Equal([mine.Number], Assert.Single(inDhaka.OnTheWay).Orders.Select(order => order.Number));
        var ctg = Assert.Single(inChattogram.OnTheWay);
        Assert.Equal([elsewhere.Number], ctg.Orders.Select(order => order.Number));
        Assert.Equal(chattogram.BaseDeliveryFee, ctg.Fee);
        Assert.Equal(chattogram.ExtraShopFee, inChattogram.ExtraShopFee);
        Assert.Empty(acrossOperators.OnTheWay);
    }

    [Fact]
    public async Task The_customer_app_installs_from_its_manifest_and_has_an_offline_page()
    {
        WebAppFactory.RequireDatabase();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://dhaka.localhost") });

        var manifest = await client.GetAsync("/manifest.webmanifest", Cancel);

        Assert.Equal(HttpStatusCode.OK, manifest.StatusCode);
        Assert.Equal("application/manifest+json", manifest.Content.Headers.ContentType?.MediaType);
        var app = JsonDocument.Parse(await manifest.Content.ReadAsStringAsync(Cancel)).RootElement;
        Assert.Equal("/Customer", app.GetProperty("start_url").GetString());
        Assert.Equal("standalone", app.GetProperty("display").GetString());
        var icons = app.GetProperty("icons").EnumerateArray().ToList();
        Assert.Contains(icons, icon => icon.GetProperty("sizes").GetString() == "192x192");
        Assert.Contains(icons, icon => icon.GetProperty("sizes").GetString() == "512x512");
        foreach (var icon in icons)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(icon.GetProperty("src").GetString(), Cancel)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/sw.js", Cancel)).StatusCode);
        Assert.Contains("No connection", await client.GetStringAsync("/offline.html", Cancel));
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private async Task<CustomerDeliveries> DeliveriesAsync(string slug, long customerId)
    {
        await using var scope = await ScopeAsync(slug);

        return await scope.ServiceProvider.GetRequiredService<CustomerDeliveriesHandler>().HandleAsync(customerId, Cancel);
    }

    private async Task<Created> CreateAsync(
        string apiKey,
        string phone,
        int packages = 1,
        decimal cod = 0,
        string speed = "combine",
        string area = Area)
    {
        var order = new
        {
            Customer = new { Name = "Deliveries Customer", Phone = phone },
            Address = new { Area = area, Line1 = "House 7, Road 2" },
            Packages = Enumerable.Range(1, packages).Select(_ => new { Description = "Parcel", WeightGrams = 400 }),
            CodAmount = cod,
            Speed = speed
        };
        var response = await factory.ClientFor(apiKey).PostAsJsonAsync("/api/v1/orders", order, Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<Created>(Cancel))!;
    }

    private async Task MoveAsync(string slug, string number, OrderStatus status)
    {
        await using var scope = await ScopeAsync(slug);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var order = await db.Orders.SingleAsync(o => o.Number == number, Cancel);
        Assert.True(order.MoveTo(status).IsSuccess);
        await db.SaveChangesAsync(Cancel);
    }

    private async Task ChangeGroupAsync(string slug, string number, Action<DeliveryGroup> change)
    {
        await using var scope = await ScopeAsync(slug);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        change(await db.DeliveryGroups.SingleAsync(g => g.Number == number, Cancel));
        await db.SaveChangesAsync(Cancel);
    }

    private async Task<DeliveryGroup> GroupAsync(string slug, string number)
    {
        await using var scope = await ScopeAsync(slug);

        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().DeliveryGroups
            .AsNoTracking()
            .SingleAsync(g => g.Number == number, Cancel);
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

    private static DateOnly LocalDate(TenantInfo tenant, DateTime utc)
    {
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById(tenant.TimeZone)));
    }

    private static string NewPhone()
    {
        return "018" + Random.Shared.Next(0, 100_000_000).ToString("D8");
    }

    private sealed record Created(string Number, long CustomerId);
}
