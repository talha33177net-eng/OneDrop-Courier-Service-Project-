using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Application.Network.PickupRoutes;
using Domain.Customers;
using Domain.Delivery;
using Domain.Network;
using Domain.Payments;
using Infrastructure.Persistence;

namespace Integration.Tests;

/// <summary>
/// Task 3.8: a customer who refused pays the fee in advance until they have accepted enough deliveries since, and a
/// shop that is late too often brings its parcels to the hub. Same set-up as the trip tests: a hub of each test's own.
/// </summary>
public partial class TripTests
{
    private static readonly JsonSerializerOptions ApiJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [Fact]
    public async Task After_a_refusal_the_customer_pays_in_advance_until_enough_deliveries_are_accepted_since()
    {
        WebAppFactory.RequireDatabase();
        var dhaka = await TenantAsync("dhaka");
        var hub = await NewHubAsync();
        var rider = await NewRiderAsync(hub, "Trust rider", new TripLoad(30, 25_000));
        var phone = NewPhone();

        // Four deliveries in four areas: four visits on one trip, the first refused, then one accepted after another
        var areas = new List<TestHub> { hub };
        for (var i = 1; i <= dhaka.TrustedAgainAfterDeliveries; i++)
        {
            areas.Add(await NewAreaAsync(hub));
        }

        var orders = new List<string>();
        foreach (var area in areas)
        {
            orders.Add(await CreateAsync(WebAppFactory.DhakaFashion, phone, area, cod: 500));
        }

        var deliveries = new List<string>();
        foreach (var order in orders)
        {
            deliveries.Add(await DueAsync(hub, [order]));
        }

        await PlanAsync("dhaka", hub);
        Assert.True((await StartAsync("dhaka", rider.UserId!.Value)).IsSuccess);

        Assert.True((await VisitAsync(rider, deliveries[0], refused: [orders[0]])).IsSuccess);
        var afterRefusal = await WaitsForAsync(WebAppFactory.DhakaGadget, phone, hub, "House 1, Road 9");

        var steps = new List<CustomerStep>();
        foreach (var delivery in deliveries.Skip(1))
        {
            Assert.Equal(StopOutcome.Delivered, (await VisitAsync(rider, delivery, refused: [])).Value.Outcome);
            steps.Add(await WaitsForAsync(WebAppFactory.DhakaBeauty, phone, hub, $"House {steps.Count + 2}, Road 9"));
        }

        // The refusal asks for the advance at another shop; it stays until the tenant's count of deliveries since
        Assert.Equal(CustomerStep.PayInAdvance, afterRefusal);
        Assert.Equal(
            [.. Enumerable.Repeat(CustomerStep.PayInAdvance, dhaka.TrustedAgainAfterDeliveries - 1), CustomerStep.None],
            steps);

        // Back to normal, the shop's own request is heard again
        Assert.Equal(
            CustomerStep.PayInAdvance,
            await WaitsForAsync(WebAppFactory.DhakaBeauty, phone, hub, "House 20, Road 9", feeInAdvance: true));
    }

    [Fact]
    public async Task A_shop_late_too_often_brings_its_parcels_to_the_hub_and_is_told_until_when()
    {
        WebAppFactory.RequireDatabase();
        var dhaka = await TenantAsync("dhaka");
        var hub = await NewHubAsync();
        var routeId = await NewRouteAsync(hub);
        var shop = await NewShopAsync(hub, withLogin: true, pickupArea: hub.Area);
        var orders = new List<string>();
        for (var i = 0; i <= dhaka.DropOffAfterLateHandovers; i++)
        {
            orders.Add(await CreateAsync(shop.ApiKey, NewPhone(), hub));
        }

        // One late handover short of the limit, and an older one outside the window: the van still calls
        var now = DateTime.UtcNow;
        var recent = Enumerable.Range(1, dhaka.DropOffAfterLateHandovers - 1).Select(days => now.AddDays(-days)).ToList();
        await MarkShopLateAsync(orders[0], now.AddDays(-dhaka.LateHandoverWindowDays - 1));
        for (var i = 0; i < recent.Count; i++)
        {
            await MarkShopLateAsync(orders[i + 1], recent[i]);
        }

        var before = Assert.Single((await RouteSheetAsync(routeId)).Stops);

        // One more within the window: the shop drops off until the oldest counting one leaves it
        var oldest = now.AddDays(-5);
        await MarkShopLateAsync(orders[^1], oldest);
        var sheet = await RouteSheetAsync(routeId);
        var after = Assert.Single(sheet.Stops);
        var listed = await RouteListAsync(routeId);
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(dhaka.TimeZone);
        var until = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(oldest.AddDays(dhaka.LateHandoverWindowDays), timeZone));

        Assert.Equal((true, orders.Count), (before.Visit, before.Packages));
        Assert.Equal((false, 0, (DateOnly?)until), (after.Visit, after.Packages, after.DropsOffUntil));
        Assert.Equal(orders.Count, after.Orders.Count);
        Assert.Equal((0, 0, 0), (listed.Stops, listed.Orders, listed.Packages));

        // The shop is told on its own page, with the hub to bring its parcels to; another shop is not
        var page = await (await SignInAsync("dhaka", shop.Email!)).GetStringAsync("/Merchant/Orders", Cancel);
        var other = await NewShopAsync(hub, withLogin: true, pickupArea: hub.Area);
        var otherPage = await (await SignInAsync("dhaka", other.Email!)).GetStringAsync("/Merchant/Orders", Cancel);
        Assert.Contains($"Bring your parcels to the hub until {until:dddd d MMMM}.", page);
        Assert.Contains($"Trip test hub {hub.Code}", page);
        Assert.DoesNotContain("Bring your parcels to the hub", otherPage);
    }

    /// <summary>The visit holding <paramref name="delivery"/>: the customer refuses some orders and pays in cash.</summary>
    private async Task<Domain.Common.Result<Application.Delivery.Door.DoorResult>> VisitAsync(
        RiderLogin rider,
        string delivery,
        string[] refused)
    {
        var userId = rider.UserId!.Value;
        var stop = (await RiderTodayAsync("dhaka", userId))!.Stops.Single(s => s.Deliveries.Contains(delivery));
        var due = (await DoorAsync("dhaka", door => door.DueAsync(userId, stop.Key, refused, Cancel))).Value;

        return await DoorAsync(
            "dhaka",
            door => door.HandOverAsync(userId, stop.Key, refused, due.Total, PaymentMethod.Cash, Cancel));
    }

    /// <summary>
    /// What a new cash order of <paramref name="phone"/> from <paramref name="apiKey"/>'s shop waits for. Each check goes
    /// to an address of its own: an order joining a delivery whose advance is unpaid waits for that payment (3.6b).
    /// </summary>
    private async Task<CustomerStep> WaitsForAsync(
        string apiKey,
        string phone,
        TestHub hub,
        string line1,
        bool feeInAdvance = false)
    {
        var order = new
        {
            Customer = new { Name = "Trip Customer", Phone = phone },
            Address = new { Area = hub.Area, Line1 = line1 },
            Packages = new[] { new { Description = "Parcel", WeightGrams = 400 } },
            CodAmount = 700,
            Speed = "combine",
            FeeInAdvance = feeInAdvance
        };
        var response = await factory.ClientFor(apiKey).PostAsJsonAsync("/api/v1/orders", order, Cancel);

        return (await response.Content.ReadFromJsonAsync<Waiting>(ApiJson, Cancel))!.WaitsFor;
    }

    /// <summary>Another area in <paramref name="hub"/>'s zone: the same customer there is another visit.</summary>
    private async Task<TestHub> NewAreaAsync(TestHub hub)
    {
        await using var scope = await ScopeAsync("dhaka");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var zoneId = await db.Areas.Where(a => a.Name == hub.Area).Select(a => a.ZoneId).SingleAsync(Cancel);
        var area = new Area($"Trip test {hub.Code} {Guid.NewGuid():N}"[..30], zoneId);
        db.Areas.Add(area);
        await db.SaveChangesAsync(Cancel);

        return hub with { Area = area.Name };
    }

    /// <summary>A pickup route for <paramref name="hub"/>'s zone, so its shops are on a route sheet.</summary>
    private async Task<long> NewRouteAsync(TestHub hub)
    {
        await using var scope = await ScopeAsync("dhaka");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var zoneId = await db.Areas.Where(a => a.Name == hub.Area).Select(a => a.ZoneId).SingleAsync(Cancel);
        var route = new PickupRoute(zoneId, new TimeOnly(13, 0));
        db.PickupRoutes.Add(route);
        await db.SaveChangesAsync(Cancel);

        return route.Id;
    }

    /// <summary>Records the order as left behind by its shop at <paramref name="on"/>, as Start trip would.</summary>
    private async Task MarkShopLateAsync(string number, DateTime on)
    {
        await using var scope = await ScopeAsync("dhaka");
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [Orders].[Order] SET [ShopLateOn] = {on}, [LeftBehindOn] = {on} WHERE [Number] = {number}",
            Cancel);
    }

    private async Task<bool[]> ShopLateAsync(params string[] numbers)
    {
        await using var scope = await ScopeAsync("dhaka");
        var late = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Orders
            .Where(o => numbers.Contains(o.Number))
            .ToDictionaryAsync(o => o.Number, o => o.ShopLateOn != null, Cancel);

        return [.. numbers.Select(number => late[number])];
    }

    private async Task<PickupRouteSheet> RouteSheetAsync(long routeId)
    {
        await using var scope = await ScopeAsync("dhaka");

        return (await scope.ServiceProvider.GetRequiredService<PickupRoutesHandler>().GetAsync(routeId, Cancel))!;
    }

    private async Task<PickupRouteSummary> RouteListAsync(long routeId)
    {
        await using var scope = await ScopeAsync("dhaka");

        return (await scope.ServiceProvider.GetRequiredService<PickupRoutesHandler>().ListAsync(Cancel))
            .Single(route => route.Id == routeId);
    }

    private sealed record Waiting(string Number, CustomerStep WaitsFor);
}
