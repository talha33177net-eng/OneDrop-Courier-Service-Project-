using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Application.Abstractions;
using Application.Common;
using Application.Delivery.HubTrips;
using Application.Delivery.PlanTrips;
using Application.Delivery.RiderDay;
using Application.Network.HubScan;
using Domain.Customers;
using Domain.Delivery;
using Domain.Grouping;
using Domain.Network;
using Domain.Orders;
using Infrastructure.Identity;
using Infrastructure.MultiTenancy;
using Infrastructure.Persistence;

namespace Integration.Tests;

/// <summary>
/// Task 3.4: riders, trips and the plan that fills them. Every test builds a hub, zone and area of its own, so the
/// planner only ever sees that test's riders and deliveries, never what earlier runs left in the database.
/// Deliveries are made due today by moving their delivery day to today in SQL, as the lock job would have left them.
/// </summary>
public partial class TripTests(WebAppFactory factory)
{
    private const string Password = "OneDrop#2026";

    [Fact]
    public async Task Due_deliveries_go_on_the_hubs_riders_within_each_bike_and_the_rest_wait()
    {
        WebAppFactory.RequireDatabase();
        var hub = await NewHubAsync();
        await NewRiderAsync(hub, "A rider", new TripLoad(3, 25_000));
        await NewRiderAsync(hub, "B rider", new TripLoad(2, 25_000));
        var phone = NewPhone();
        var three = await DueAsync(hub, [await CreateAsync(WebAppFactory.DhakaFashion, phone, hub, packages: 2), await CreateAsync(WebAppFactory.DhakaGadget, phone, hub)]);
        var one = await DueAsync(hub, [await CreateAsync(WebAppFactory.DhakaBeauty, NewPhone(), hub)]);
        var two = await DueAsync(hub, [await CreateAsync(WebAppFactory.DhakaBeauty, NewPhone(), hub, packages: 2)]);
        var notHere = await DueAsync(hub, [await CreateAsync(WebAppFactory.DhakaGadget, NewPhone(), hub)], receive: false);
        var notDue = await CreateAsync(WebAppFactory.DhakaFashion, NewPhone(), hub);
        await ReceiveAsync(hub, $"{notDue}-1");

        var planned = await PlanAsync("dhaka", hub);
        var again = await PlanAsync("dhaka", hub);
        var today = (await TodayAsync("dhaka", hub))!;

        Assert.Equal(new TripPlanningResult(2, 1), planned);
        Assert.Equal(new TripPlanningResult(0, 1), again);
        Assert.Equal(["A rider", "B rider"], today.Riders.Select(r => r.Rider));
        var (a, b) = (today.Riders[0], today.Riders[1]);
        Assert.Equal((TripStatus.Planned, three, new TripLoad(3, 1_200)), (a.Status, a.Stops.Single().Delivery, a.Carried));
        Assert.Equal((TripStatus.Planned, one, 1), (b.Status, b.Stops.Single().Delivery, b.Carried.Parcels));
        Assert.Equal((3, 3), (a.Stops[0].ParcelsHere, a.Stops[0].Parcels));
        Assert.StartsWith($"{hub.Code}-", a.Stops[0].Shelf);
        Assert.Equal([two, notHere], today.Waiting.Select(w => w.Delivery));
        Assert.Equal((2, 2, 0, 1), (today.Waiting[0].ParcelsHere, today.Waiting[0].Parcels, today.Waiting[1].ParcelsHere, today.Waiting[1].Parcels));
        var notDueGroup = (await GroupOfAsync(notDue)).Number;
        Assert.DoesNotContain(today.Waiting, w => w.Delivery == notDueGroup);
        Assert.Equal(2, await StopCountAsync(hub));
    }

    [Fact]
    public async Task The_rider_sees_the_stops_and_starting_takes_out_only_what_is_on_the_shelf()
    {
        WebAppFactory.RequireDatabase();
        var dhaka = await TenantAsync("dhaka");
        var hub = await NewHubAsync();
        var rider = await NewRiderAsync(hub, "Rider", new TripLoad(30, 25_000));
        var phone = NewPhone();
        var fashion = await CreateAsync(WebAppFactory.DhakaFashion, phone, hub, cod: 1500);
        var gadget = await CreateAsync(WebAppFactory.DhakaGadget, phone, hub, packages: 2, cod: 2400);
        var both = await DueAsync(hub, [fashion, gadget], receive: false);
        await ReceiveAsync(hub, $"{fashion}-1");
        await ReceiveAsync(hub, $"{gadget}-1");
        var single = await CreateAsync(WebAppFactory.DhakaBeauty, NewPhone(), hub, cod: 650);
        var alone = await DueAsync(hub, [single]);
        var partial = await CreateAsync(WebAppFactory.DhakaBeauty, NewPhone(), hub, packages: 2);
        var nothingReady = await DueAsync(hub, [partial], receive: false);
        await ReceiveAsync(hub, $"{partial}-1");
        await PlanAsync("dhaka", hub);

        var before = (await RiderTodayAsync("dhaka", rider.UserId!.Value))!;
        var started = await StartAsync("dhaka", rider.UserId.Value);
        var again = await StartAsync("dhaka", rider.UserId.Value);
        var after = (await RiderTodayAsync("dhaka", rider.UserId.Value))!;

        Assert.Equal((TripStatus.Planned, 3), (before.Status, before.Stops.Count));
        var stop = before.Stops.Single(s => s.Delivery == both);
        Assert.Equal((dhaka.BaseDeliveryFee + dhaka.ExtraShopFee, 3900m), (stop.Fee, stop.Cod));
        Assert.Equal((false, true), (stop.Orders.Single(o => o.Number == gadget).Ready, stop.Orders.Single(o => o.Number == fashion).Ready));
        Assert.Equal([$"{gadget}-1", $"{gadget}-2"], stop.Orders.Single(o => o.Number == gadget).Labels);
        Assert.Equal(("Trip Customer", phone, "House 12, Road 4", hub.Area), (stop.Recipient, stop.Phone, stop.Address, stop.Area));
        Assert.StartsWith($"{hub.Code}-", stop.Shelf);
        Assert.Equal(new StartedTrip(2, 2, 1), started.Value);
        Assert.Equal("trip.notPlanned", again.Error!.Code);
        Assert.Equal((TripStatus.Out, 2), (after.Status, after.Stops.Count));
        var taken = after.Stops.Single(s => s.Delivery == both);
        Assert.Equal((fashion, dhaka.BaseDeliveryFee, 1500m), (taken.Orders.Single().Number, taken.Fee, taken.Cod));
        Assert.Equal(dhaka.BaseDeliveryFee * 2 + 1500 + 650, after.ToCollect);
        Assert.Equal(
            [OrderStatus.OutForDelivery, OrderStatus.PickedUp, OrderStatus.OutForDelivery, OrderStatus.PickedUp],
            await StatusesAsync(fashion, gadget, single, partial));
        var (bothGroup, aloneGroup, waitingGroup) = (await GroupOfAsync(fashion), await GroupOfAsync(single), await GroupOfAsync(partial));
        Assert.Equal((DeliveryGroupStatus.Dispatched, null), (bothGroup.Status, bothGroup.Shelf));
        Assert.Equal(DeliveryGroupStatus.Dispatched, aloneGroup.Status);
        Assert.Equal(DeliveryGroupStatus.Locked, waitingGroup.Status);
        Assert.NotNull(waitingGroup.Shelf);
        var hubAfter = (await TodayAsync("dhaka", hub))!;
        var outRow = hubAfter.Riders.Single();
        Assert.Equal((TripStatus.Out, new TripLoad(2, 800)), (outRow.Status, outRow.Carried));
        Assert.Equal((1, 3, (string?)null), outRow.Stops.Where(s => s.Delivery == both).Select(s => (s.ParcelsTaken, s.Parcels, s.Shelf)).Single());
        Assert.Equal([nothingReady], hubAfter.Waiting.Select(w => w.Delivery));
        Assert.Equal(new TripPlanningResult(0, 1), await PlanAsync("dhaka", hub));
        Assert.Contains(alone, after.Stops.Select(s => s.Delivery));
    }

    [Fact]
    public async Task A_trip_left_planned_on_an_earlier_day_is_cancelled_and_its_delivery_goes_out_today()
    {
        WebAppFactory.RequireDatabase();
        var hub = await NewHubAsync();
        var rider = await NewRiderAsync(hub, "Rider", new TripLoad(30, 25_000));
        var today = await TodayDateAsync();
        var number = await CreateAsync(WebAppFactory.DhakaFashion, NewPhone(), hub);
        var delivery = await DueAsync(hub, [number], deliveryDay: today.AddDays(-1));
        long oldTripId;
        await using (var scope = await ScopeAsync("dhaka"))
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var trip = Trip.Plan(rider.Rider, today.AddDays(-1));
            db.Trips.Add(trip);
            await db.SaveChangesAsync(Cancel);
            db.TripStops.Add(trip.Add(await db.DeliveryGroups.SingleAsync(g => g.Number == delivery, Cancel)).Value);
            await db.SaveChangesAsync(Cancel);
            oldTripId = trip.Id;
        }

        var planned = await PlanAsync("dhaka", hub);
        var todayTrips = (await TodayAsync("dhaka", hub))!;

        Assert.Equal(new TripPlanningResult(1, 0), planned);
        Assert.Equal([delivery], todayTrips.Riders.Single().Stops.Select(s => s.Delivery));
        await using var check = await ScopeAsync("dhaka");
        var oldTrip = await check.ServiceProvider.GetRequiredService<AppDbContext>().Trips.AsNoTracking().SingleAsync(t => t.Id == oldTripId, Cancel);
        Assert.Equal(TripStatus.Cancelled, oldTrip.Status);
    }

    [Fact]
    public async Task Another_operator_sees_neither_the_hubs_trips_nor_the_riders_day()
    {
        WebAppFactory.RequireDatabase();
        var hub = await NewHubAsync();
        var rider = await NewRiderAsync(hub, "Rider", new TripLoad(30, 25_000));
        await DueAsync(hub, [await CreateAsync(WebAppFactory.DhakaFashion, NewPhone(), hub)]);

        var fromChattogram = await PlanAsync("chattogram", hub);
        var tripsFromChattogram = await TodayAsync("chattogram", hub);
        var dayFromChattogram = await RiderTodayAsync("chattogram", rider.UserId!.Value);
        var startFromChattogram = await StartAsync("chattogram", rider.UserId.Value);

        Assert.Null(fromChattogram);
        Assert.Null(tripsFromChattogram);
        Assert.Null(dayFromChattogram);
        Assert.Equal("trip.none", startFromChattogram.Error!.Code);
        Assert.Equal(0, await StopCountAsync(hub));
    }

    [Fact]
    public async Task Hub_staff_plan_on_the_page_the_rider_starts_the_trip_on_theirs_and_nobody_else_gets_in()
    {
        WebAppFactory.RequireDatabase();
        var hub = await NewHubAsync();
        var rider = await NewRiderAsync(hub, "Page rider", new TripLoad(30, 25_000));
        var number = await CreateAsync(WebAppFactory.DhakaGadget, NewPhone(), hub, cod: 900);
        var delivery = await DueAsync(hub, [number]);
        var staff = await SignInAsync("dhaka", "hub@dhaka.onedrop.test");
        var riderClient = await SignInAsync("dhaka", rider.Email);
        var chattogramHub = await SignInAsync("chattogram", "hub@chattogram.onedrop.test");
        var anonymous = factory.CreateClient(Options("dhaka"));
        var url = $"/Hub/Trips?hub={hub.Code}";

        var beforePlan = await staff.GetStringAsync(url, Cancel);
        await PostAsync(staff, url, $"{url}&handler=Plan");
        var afterPlan = await staff.GetStringAsync(url, Cancel);
        var riderPage = await riderClient.GetStringAsync("/Rider", Cancel);
        await PostAsync(riderClient, "/Rider", "/Rider?handler=Start");
        var riderOut = await riderClient.GetStringAsync("/Rider", Cancel);

        Assert.Contains("Page rider", beforePlan);
        Assert.Contains("No trip yet", beforePlan);
        Assert.Contains(delivery, beforePlan);
        Assert.Contains(delivery, afterPlan);
        Assert.Contains("Planned", afterPlan);
        Assert.Contains("Start trip", riderPage);
        Assert.Contains("Trip Customer", riderPage);
        Assert.Contains($"{number}-1", riderPage);
        Assert.Contains("/rider.webmanifest", riderPage);
        Assert.Contains("With you", riderOut);
        Assert.DoesNotContain("Start trip", riderOut);
        Assert.Equal([OrderStatus.OutForDelivery], await StatusesAsync(number));
        Assert.Equal(HttpStatusCode.NotFound, (await chattogramHub.GetAsync(url, Cancel)).StatusCode);
        Assert.StartsWith("/Account/AccessDenied", (await riderClient.GetAsync(url, Cancel)).Headers.Location!.PathAndQuery);
        Assert.StartsWith("/Account/AccessDenied", (await staff.GetAsync("/Rider", Cancel)).Headers.Location!.PathAndQuery);
        Assert.StartsWith("/Account/Login", (await anonymous.GetAsync("/Rider", Cancel)).Headers.Location!.PathAndQuery);
        var manifest = await factory.CreateClient().GetStringAsync("/rider.webmanifest", Cancel);
        Assert.Contains("\"start_url\": \"/Rider\"", manifest);
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    /// <summary>A hub of this test's own, serving one zone with one area.</summary>
    private async Task<TestHub> NewHubAsync()
    {
        await using var scope = await ScopeAsync("dhaka");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var code = "T" + Guid.NewGuid().ToString("N")[..7].ToUpperInvariant();
        var hub = new Hub(code, $"Trip test hub {code}", "Test road");
        db.Hubs.Add(hub);
        await db.SaveChangesAsync(Cancel);
        var zone = new Zone(code, $"Trip test {code}", hub.Id);
        db.Zones.Add(zone);
        await db.SaveChangesAsync(Cancel);
        var area = new Area($"Trip test {code}", zone.Id);
        db.Areas.Add(area);
        await db.SaveChangesAsync(Cancel);

        return new TestHub(hub.Id, code, area.Name);
    }

    /// <summary>A rider of <paramref name="hub"/> with a login of their own.</summary>
    private async Task<RiderLogin> NewRiderAsync(TestHub hub, string name, TripLoad limit)
    {
        await using var scope = await ScopeAsync("dhaka");
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var email = $"rider-{Guid.NewGuid():N}@dhaka.onedrop.test";
        var user = new AppUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = name,
            TenantId = (await TenantAsync("dhaka")).Id,
            Created = DateTime.UtcNow
        };
        Assert.True((await users.CreateAsync(user, Password)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, Roles.Rider)).Succeeded);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rider = new Rider(hub.Id, name, PhoneNumber.Parse(NewPhone()).Value, limit, user.Id);
        db.Riders.Add(rider);
        await db.SaveChangesAsync(Cancel);

        return new RiderLogin(rider, email);
    }

    /// <summary>
    /// Makes the delivery of <paramref name="orders"/> closed and due on <paramref name="deliveryDay"/> (today when
    /// not given), as the lock job would have left it, and scans every parcel in at the hub unless told not to.
    /// Returns the delivery's number.
    /// </summary>
    private async Task<string> DueAsync(TestHub hub, string[] orders, bool receive = true, DateOnly? deliveryDay = null)
    {
        var group = await GroupOfAsync(orders[0]);
        var tenant = await TenantAsync("dhaka");
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(tenant.TimeZone);
        var day = (deliveryDay ?? await TodayDateAsync()).ToDateTime(TimeOnly.MinValue);
        var locksAt = new DateTimeOffset(day, timeZone.GetUtcOffset(day)).UtcDateTime;
        var openedOn = locksAt.AddDays(-2);
        await using (var scope = await ScopeAsync("dhaka"))
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [Grouping].[DeliveryGroup] SET [Status] = 2, [OpenedOn] = {openedOn}, [LocksAt] = {locksAt}, [LockedOn] = {locksAt} WHERE [Id] = {group.Id}",
                Cancel);
        }

        if (receive)
        {
            foreach (var number in orders)
            {
                foreach (var sequence in await PackageSequencesAsync(number))
                {
                    Assert.True((await ReceiveAsync(hub, $"{number}-{sequence}")).IsSuccess);
                }
            }
        }

        return group.Number;
    }

    private async Task<DateOnly> TodayDateAsync()
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById((await TenantAsync("dhaka")).TimeZone);

        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone));
    }

    private async Task<TripPlanningResult?> PlanAsync(string slug, TestHub hub)
    {
        await using var scope = await ScopeAsync(slug);

        return await scope.ServiceProvider.GetRequiredService<HubTripsHandler>().PlanAsync(hub.Code, Cancel);
    }

    private async Task<HubTrips?> TodayAsync(string slug, TestHub hub)
    {
        await using var scope = await ScopeAsync(slug);

        return await scope.ServiceProvider.GetRequiredService<HubTripsHandler>().TodayAsync(hub.Code, Cancel);
    }

    private async Task<RiderToday?> RiderTodayAsync(string slug, long userId)
    {
        await using var scope = await ScopeAsync(slug);

        return await scope.ServiceProvider.GetRequiredService<RiderDayHandler>().TodayAsync(userId, Cancel);
    }

    private async Task<Domain.Common.Result<StartedTrip>> StartAsync(string slug, long userId)
    {
        await using var scope = await ScopeAsync(slug);

        return await scope.ServiceProvider.GetRequiredService<RiderDayHandler>().StartAsync(userId, Cancel);
    }

    private async Task<Domain.Common.Result<ParcelScan>> ReceiveAsync(TestHub hub, string label)
    {
        await using var scope = await ScopeAsync("dhaka");

        return await scope.ServiceProvider.GetRequiredService<HubScanHandler>().ReceiveAsync(hub.Code, label, Cancel);
    }

    private async Task<int> StopCountAsync(TestHub hub)
    {
        await using var scope = await ScopeAsync("dhaka");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.TripStops.CountAsync(stop => db.Trips.Any(trip => trip.Id == stop.TripId && trip.HubId == hub.Id), Cancel);
    }

    private async Task<int[]> PackageSequencesAsync(string number)
    {
        await using var scope = await ScopeAsync("dhaka");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.Packages
            .Where(p => db.Orders.Any(o => o.Id == p.OrderId && o.Number == number))
            .Select(p => p.Sequence)
            .ToArrayAsync(Cancel);
    }

    private async Task<DeliveryGroup> GroupOfAsync(string number)
    {
        await using var scope = await ScopeAsync("dhaka");

        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Orders
            .Where(o => o.Number == number)
            .Select(o => o.DeliveryGroup!)
            .AsNoTracking()
            .SingleAsync(Cancel);
    }

    private async Task<OrderStatus[]> StatusesAsync(params string[] numbers)
    {
        await using var scope = await ScopeAsync("dhaka");
        var statuses = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Orders
            .Where(o => numbers.Contains(o.Number))
            .ToDictionaryAsync(o => o.Number, o => o.Status, Cancel);

        return [.. numbers.Select(number => statuses[number])];
    }

    private async Task<string> CreateAsync(string apiKey, string phone, TestHub hub, int packages = 1, decimal cod = 0)
    {
        var order = new
        {
            Customer = new { Name = "Trip Customer", Phone = phone },
            Address = new { Area = hub.Area, Line1 = "House 12, Road 4" },
            Packages = Enumerable.Range(1, packages).Select(_ => new { Description = "Parcel", WeightGrams = 400 }),
            CodAmount = cod
        };
        var response = await factory.ClientFor(apiKey).PostAsJsonAsync("/api/v1/orders", order, Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<Created>(Cancel))!.Number;
    }

    /// <summary>Posts a page's form: the anti-forgery token from <paramref name="page"/>, sent to <paramref name="action"/>.</summary>
    private static async Task PostAsync(HttpClient client, string page, string action)
    {
        var token = Token().Match(await client.GetStringAsync(page, Cancel)).Groups[1].Value;
        var fields = new Dictionary<string, string> { ["__RequestVerificationToken"] = token };
        var response = await client.PostAsync(action, new FormUrlEncodedContent(fields), Cancel);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    /// <summary>Signs a staff user or rider in on their operator's subdomain. Keeps the sign-in cookie.</summary>
    private async Task<HttpClient> SignInAsync(string slug, string email)
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
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(await TenantAsync(slug));

        return scope;
    }

    private async Task<TenantInfo> TenantAsync(string slug)
    {
        return (await factory.Services.GetRequiredService<ITenantCatalog>().FindBySlugAsync(slug, Cancel))!;
    }

    private static string NewPhone()
    {
        return "015" + Random.Shared.Next(0, 100_000_000).ToString("D8");
    }

    [GeneratedRegex("""name="__RequestVerificationToken" type="hidden" value="([^"]+)""")]
    private static partial Regex Token();

    private sealed record TestHub(long Id, string Code, string Area);

    private sealed record RiderLogin(Rider Rider, string Email)
    {
        public long? UserId => Rider.UserId;
    }

    private sealed record Created(string Number);
}
