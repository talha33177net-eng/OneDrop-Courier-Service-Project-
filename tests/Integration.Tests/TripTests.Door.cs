using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Application.Delivery.Door;
using Application.Delivery.PlanTrips;
using Application.Delivery.RiderDay;
using Application.Grouping.CustomerDeliveries;
using Application.Network.HubScan;
using Domain.Common;
using Domain.Delivery;
using Domain.Grouping;
using Domain.Orders;
using Domain.Payments;
using Infrastructure.Persistence;

namespace Integration.Tests;

/// <summary>Task 3.5: the rider at the door. Same set-up as the trip tests: a hub, zone and area of each test's own.</summary>
public partial class TripTests
{
    [Fact]
    public async Task One_door_is_one_fee_and_the_customer_pays_only_for_what_they_take()
    {
        WebAppFactory.RequireDatabase();
        var dhaka = await TenantAsync("dhaka");
        var hub = await NewHubAsync();
        var rider = await NewRiderAsync(hub, "Door rider", new TripLoad(30, 25_000));
        var phone = NewPhone();
        var fashion = await CreateAsync(WebAppFactory.DhakaFashion, phone, hub, cod: 1500);

        // The same customer and area, the address spelt another way and fast: a delivery of its own
        var gadget = await CreateAsync(
            WebAppFactory.DhakaGadget,
            phone,
            hub,
            cod: 800,
            line1: "Flat 3B, House 12, Road 4",
            speed: "fast");
        var waiting = await DueAsync(hub, [fashion]);
        var fast = await DueAsync(hub, [gadget]);
        await PlanAsync("dhaka", hub);
        Assert.Equal(new StartedTrip(2, 2, 0, 0), (await StartAsync("dhaka", rider.UserId!.Value)).Value);

        var stop = Assert.Single((await RiderTodayAsync("dhaka", rider.UserId.Value))!.Stops);
        var due = (await DoorAsync("dhaka", door => door.DueAsync(rider.UserId.Value, stop.Key, [gadget], Cancel))).Value;
        var wrongAmount = await DoorAsync("dhaka", door => door.HandOverAsync(rider.UserId.Value, stop.Key, [gadget], 1000, PaymentMethod.Cash, Cancel));
        var handedOver = await DoorAsync("dhaka", door => door.HandOverAsync(rider.UserId.Value, stop.Key, [gadget], due.Total, PaymentMethod.Cash, Cancel));
        var again = await DoorAsync("dhaka", door => door.HandOverAsync(rider.UserId.Value, stop.Key, [], due.Total, PaymentMethod.Cash, Cancel));
        var after = (await RiderTodayAsync("dhaka", rider.UserId.Value))!;

        // One visit for both deliveries: the fast fee for the first shop and the extra-shop fee for the second
        Assert.Equal([waiting, fast], stop.Deliveries);
        Assert.Equal((dhaka.FastDeliveryFee + dhaka.ExtraShopFee, 2300m), (stop.Fee, stop.Cod));

        // Gadget BD refused: the customer pays the base fee and Fashion House's COD, and nothing else changes on a
        // wrong amount
        Assert.Equal((dhaka.BaseDeliveryFee, 1500m), (due.Fee, due.Cod));
        Assert.Equal([fashion], due.Taking.Select(o => o.Number));
        Assert.Equal([gadget], due.Refusing.Select(o => o.Number));
        Assert.Equal("door.amount", wrongAmount.Error!.Code);
        Assert.Equal(new DoorResult(StopOutcome.Delivered, due.Total, BackToShop: false, PaymentMethod.Cash), handedOver.Value);
        Assert.Equal(TripStop.AlreadyDone.Code, again.Error!.Code);
        Assert.Equal([OrderStatus.Delivered, OrderStatus.Refused], await StatusesAsync(fashion, gadget));
        Assert.Equal(
            (DeliveryGroupStatus.Delivered, DeliveryGroupStatus.Cancelled),
            ((await GroupOfAsync(fashion)).Status, (await GroupOfAsync(gadget)).Status));
        Assert.Equal(
            [new StopRow(StopOutcome.Delivered, dhaka.BaseDeliveryFee, 1500), new StopRow(StopOutcome.Delivered, 0, 0)],
            await StopsOfAsync(waiting, fast));
        Assert.Equal((TripStatus.Finished, StopOutcome.Delivered), (after.Status, after.Stops.Single().Outcome));
        Assert.Equal((dhaka.BaseDeliveryFee + 1500, 0m), (after.Collected, after.ToCollect));

        // The customer's page shows what was collected
        var customerId = (await GroupOfAsync(fashion)).CustomerId;
        var delivered = (await CustomerDeliveriesAsync(customerId)).Earlier.Single(d => d.Number == waiting);
        Assert.Equal(dhaka.BaseDeliveryFee, delivered.Fee);

        // The refused parcel comes back to the hub on its way to the shop, and is handed back there
        var back = await ReceiveAsync(hub, $"{gadget}-1");
        var returned = await HubScanAsync(scan => scan.ReturnAsync($"{gadget}-1", Cancel));
        Assert.Equal((OrderStatus.Refused, (string?)null), (back.Value.Status, back.Value.Shelf));
        Assert.Equal(OrderStatus.ReturnedToMerchant, returned.Value.Status);

        // Another operator's scope knows no trip for this rider
        var elsewhere = await DoorAsync("chattogram", door => door.DueAsync(rider.UserId.Value, stop.Key, [], Cancel));
        Assert.Equal("trip.none", elsewhere.Error!.Code);
    }

    [Fact]
    public async Task Nobody_home_is_one_free_reattempt_and_the_second_time_the_order_goes_back_to_the_shop()
    {
        WebAppFactory.RequireDatabase();
        var hub = await NewHubAsync();
        var rider = await NewRiderAsync(hub, "Door rider", new TripLoad(30, 25_000));
        var order = await CreateAsync(WebAppFactory.DhakaBeauty, NewPhone(), hub, cod: 650);
        var delivery = await DueAsync(hub, [order]);
        await PlanAsync("dhaka", hub);
        await StartAsync("dhaka", rider.UserId!.Value);

        var first = await DoorAsync("dhaka", door => door.NotHomeAsync(rider.UserId.Value, delivery, Cancel));
        var replanned = await PlanAsync("dhaka", hub);
        var back = await ReceiveAsync(hub, $"{order}-1");

        Assert.Equal(new DoorResult(StopOutcome.NotHome, 0, BackToShop: false), first.Value);
        Assert.Equal([OrderStatus.AtHub], await StatusesAsync(order));
        Assert.Equal(DeliveryGroupStatus.Locked, (await GroupOfAsync(order)).Status);
        Assert.Equal([new StopRow(StopOutcome.NotHome, 0, 0)], await StopsOfAsync(delivery));
        Assert.Equal(new TripPlanningResult(0, 0), replanned);
        Assert.NotNull(back.Value.Shelf);

        // The re-attempt on another day: today's trip becomes yesterday's
        await using (var scope = await ScopeAsync("dhaka"))
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var yesterday = (await TodayDateAsync()).AddDays(-1);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [Delivery].[TripStop] SET [DeliveryDate] = {yesterday} WHERE [TripId] IN (SELECT [Id] FROM [Delivery].[Trip] WHERE [RiderId] = {rider.Rider.Id})",
                Cancel);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [Delivery].[Trip] SET [DeliveryDate] = {yesterday} WHERE [RiderId] = {rider.Rider.Id}",
                Cancel);
        }

        Assert.Equal(new TripPlanningResult(1, 0), await PlanAsync("dhaka", hub));
        await StartAsync("dhaka", rider.UserId.Value);
        var second = await DoorAsync("dhaka", door => door.NotHomeAsync(rider.UserId.Value, delivery, Cancel));

        Assert.Equal(new DoorResult(StopOutcome.NotHome, 0, BackToShop: true), second.Value);
        Assert.Equal([OrderStatus.Refused], await StatusesAsync(order));
        Assert.Equal(DeliveryGroupStatus.Cancelled, (await GroupOfAsync(order)).Status);
    }

    [Fact]
    public async Task The_rider_checks_the_amount_on_the_page_and_hands_over_once_paid()
    {
        WebAppFactory.RequireDatabase();
        var dhaka = await TenantAsync("dhaka");
        var hub = await NewHubAsync();
        var rider = await NewRiderAsync(hub, "Door rider", new TripLoad(30, 25_000));
        var order = await CreateAsync(WebAppFactory.DhakaFashion, NewPhone(), hub, cod: 1200);
        var delivery = await DueAsync(hub, [order]);
        await PlanAsync("dhaka", hub);
        await StartAsync("dhaka", rider.UserId!.Value);
        var client = await SignInAsync("dhaka", rider.Email);

        var atTheDoor = await client.GetStringAsync("/Rider", Cancel);
        var review = await client.GetStringAsync($"/Rider?stop={delivery}", Cancel);
        var total = dhaka.BaseDeliveryFee + 1200;
        var token = Token().Match(review).Groups[1].Value;
        var handOver = await client.PostAsync(
            "/Rider?handler=HandOver",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["stop"] = delivery,
                ["collected"] = total.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["method"] = nameof(PaymentMethod.Cash),
                ["__RequestVerificationToken"] = token
            }),
            Cancel);
        var done = await client.GetStringAsync("/Rider", Cancel);

        Assert.Contains("Nobody home</button>", atTheDoor);
        Assert.Contains("name=\"refused\"", atTheDoor);
        Assert.Contains($"Collect ৳{total:N0}", review);
        Assert.Contains($"Cash ৳{total:N0} collected, hand over</button>", review);
        Assert.Contains(">bKash QR</button>", review);
        Assert.Equal(HttpStatusCode.Redirect, handOver.StatusCode);
        Assert.Contains("in cash.", done);
        Assert.Matches($@"Cash to hand in</dt>\s*<dd>৳{total:N0}</dd>", done);
        Assert.Contains("Every stop is done.", done);
        Assert.Equal([OrderStatus.Delivered], await StatusesAsync(order));
    }

    private async Task<Result<T>> DoorAsync<T>(string slug, Func<DoorHandler, Task<Result<T>>> act)
    {
        await using var scope = await ScopeAsync(slug);

        return await act(scope.ServiceProvider.GetRequiredService<DoorHandler>());
    }

    private async Task<Result<ParcelScan>> HubScanAsync(Func<HubScanHandler, Task<Result<ParcelScan>>> scan)
    {
        await using var scope = await ScopeAsync("dhaka");

        return await scan(scope.ServiceProvider.GetRequiredService<HubScanHandler>());
    }

    /// <summary>Each delivery's stops (outcome, fee and COD collected), oldest first.</summary>
    private async Task<StopRow[]> StopsOfAsync(params string[] deliveries)
    {
        await using var scope = await ScopeAsync("dhaka");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stops = await (
            from stop in db.TripStops
            join g in db.DeliveryGroups on stop.DeliveryGroupId equals g.Id
            where deliveries.Contains(g.Number)
            select new { g.Number, stop.Id, stop.Outcome, stop.FeeCollected, stop.CodCollected })
            .ToListAsync(Cancel);

        return
        [
            .. deliveries.SelectMany(number => stops
                .Where(stop => stop.Number == number)
                .OrderBy(stop => stop.Id)
                .Select(stop => new StopRow(stop.Outcome, stop.FeeCollected, stop.CodCollected)))
        ];
    }

    private async Task<CustomerDeliveries> CustomerDeliveriesAsync(long customerId)
    {
        await using var scope = await ScopeAsync("dhaka");

        return await scope.ServiceProvider.GetRequiredService<CustomerDeliveriesHandler>().HandleAsync(customerId, Cancel);
    }

    private sealed record StopRow(StopOutcome? Outcome, decimal? Fee, decimal? Cod);
}
