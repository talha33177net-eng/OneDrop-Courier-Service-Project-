using System.Net;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Application.Abstractions;
using Application.Operations.Dashboard;
using Domain.Customers;
using Domain.Delivery;
using Domain.Grouping;
using Domain.Network;
using Domain.Orders;
using Infrastructure.Persistence;
using Web.Live;

namespace Integration.Tests;

/// <summary>
/// Task 4.1: the live dashboards. Hub today and the operator's dashboard count from the data; a save that changes
/// operations tells the operator's open dashboards through SignalR, which read their counts again.
/// </summary>
public partial class TripTests
{
    [Fact]
    public async Task A_hub_counts_its_deliveries_parcels_and_riders_and_names_todays_parcels_not_here_yet()
    {
        WebAppFactory.RequireDatabase();
        var hub = await NewHubAsync();
        var other = await NewHubAsync();
        var rider = await NewRiderAsync(hub, "Rider", new TripLoad(30, 25_000));
        var openOrder = await CreateAsync(WebAppFactory.DhakaFashion, NewPhone(), hub);
        await ReceiveAsync(hub, $"{openOrder}-1");
        var phone = NewPhone();
        var fashion = await CreateAsync(WebAppFactory.DhakaFashion, phone, hub);
        var gadget = await CreateAsync(WebAppFactory.DhakaGadget, phone, hub);
        await DueAsync(hub, [fashion, gadget], receive: false);
        await ReceiveAsync(hub, $"{fashion}-1");
        var beauty = await CreateAsync(WebAppFactory.DhakaBeauty, NewPhone(), hub, packages: 2);
        await DueAsync(hub, [beauty], receive: false);
        await ReceiveAsync(other, $"{beauty}-1");
        await ReceiveAsync(other, $"{beauty}-2");
        Assert.True((await HubScanAsync(scan => scan.LoadAsync(other.Code, $"{beauty}-1", Cancel))).IsSuccess);

        var before = (await HubNowAsync("dhaka", hub))!;
        var staff = await SignInAsync("dhaka", "hub@dhaka.onedrop.test");
        var page = await staff.GetStringAsync($"/Hub?hub={hub.Code}", Cancel);
        await PlanAsync("dhaka", hub);
        await StartAsync("dhaka", rider.UserId!.Value);
        var after = (await HubNowAsync("dhaka", hub))!;

        Assert.Equal((1, 2, 2, 1, 0, 1), (before.OpenDeliveries, before.DueToday, before.ParcelsHere, before.ParcelsOnTheWay, before.RidersOut, before.Riders));
        Assert.Equal(
            new Dictionary<string, (string, ParcelPlace, string?)>
            {
                [$"{gadget}-1"] = ("Gadget BD", ParcelPlace.AtTheShop, null),
                [$"{beauty}-1"] = ("Beauty Shop", ParcelPlace.OnTheShuttle, null),
                [$"{beauty}-2"] = ("Beauty Shop", ParcelPlace.AtAnotherHub, other.Code)
            },
            before.NotHereYet.ToDictionary(p => p.Label, p => (p.Shop, p.Place, p.OtherHub)));
        Assert.Contains("3 parcels for today's deliveries are not", page);
        Assert.Contains($"At {other.Code} hub, not on the shuttle yet", page);
        Assert.Contains("data-live=\"today\"", page);
        Assert.Contains("/lib/signalr/signalr.min.js", page);

        // The ready delivery went out and Gadget BD's order follows tomorrow; Beauty Shop's parcels are still away
        Assert.Equal((1, 1, 1, 1, 1), (after.OpenDeliveries, after.DueToday, after.ParcelsHere, after.ParcelsOnTheWay, after.RidersOut));
        Assert.Equal([$"{beauty}-1", $"{beauty}-2"], after.NotHereYet.Select(p => p.Label).Order());
        Assert.Null(await HubNowAsync("chattogram", hub));
    }

    [Fact]
    public async Task Packages_per_delivery_are_counted_by_area_week_by_week_and_only_the_operators_admins_see_them()
    {
        WebAppFactory.RequireDatabase();
        var hub = await NewHubAsync();
        var rider = await NewRiderAsync(hub, "Rider", new TripLoad(30, 25_000));
        var phone = NewPhone();
        var three = await DueAsync(hub, [
            await CreateAsync(WebAppFactory.DhakaFashion, phone, hub, packages: 2, cod: 500),
            await CreateAsync(WebAppFactory.DhakaGadget, phone, hub, cod: 300)]);
        var one = await DueAsync(hub, [await CreateAsync(WebAppFactory.DhakaBeauty, NewPhone(), hub, cod: 200)]);
        await PlanAsync("dhaka", hub);
        await StartAsync("dhaka", rider.UserId!.Value);
        Assert.True((await VisitAsync(rider, three, [])).IsSuccess);
        Assert.True((await VisitAsync(rider, one, [])).IsSuccess);
        await using (var scope = await ScopeAsync("dhaka"))
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE s SET [DeliveryDate] = DATEADD(DAY, -7, s.[DeliveryDate]) FROM [Delivery].[TripStop] s JOIN [Grouping].[DeliveryGroup] g ON g.[Id] = s.[DeliveryGroupId] WHERE g.[Number] = {one}",
                Cancel);
        }

        var now = await OperatorNowAsync("dhaka");
        var admin = await SignInAsync("dhaka", "admin@dhaka.onedrop.test");
        var home = await admin.GetAsync("/", Cancel);
        var page = await admin.GetStringAsync("/Admin", Cancel);
        var staff = await SignInAsync("dhaka", "hub@dhaka.onedrop.test");
        var chattogramAdmin = await SignInAsync("chattogram", "admin@chattogram.onedrop.test");

        var today = await TodayDateAsync();
        Assert.Equal(8, now.Weeks.Count);
        Assert.Equal((today.AddDays(-55), today.AddDays(-6), today), (now.Weeks[0].From, now.Weeks[^1].From, now.Weeks[^1].To));
        var area = Assert.Single(now.Areas, a => a.Area == hub.Area);
        Assert.Equal(new DeliveryDensity(1, 3), area.Weeks[^1]);
        Assert.Equal(new DeliveryDensity(1, 1), area.Weeks[^2]);
        Assert.All(area.Weeks.SkipLast(2), week => Assert.Equal(DeliveryDensity.None, week));
        Assert.Equal((3.0m, 1.0m), (area.Weeks[^1].PackagesPerDelivery, area.Weeks[^2].PackagesPerDelivery));
        Assert.True(now.Total[^1].Deliveries >= 1);
        var hubRow = Assert.Single(now.Hubs, h => h.Code == hub.Code);
        Assert.Equal((0, 0, 1), (hubRow.DueToday, hubRow.RidersOut, hubRow.Riders));

        Assert.Equal("/Admin", home.Headers.Location!.OriginalString);
        Assert.Contains(hub.Area, page);
        Assert.Contains($"/Hub?hub={hub.Code}", page);
        Assert.Contains("Packages per delivery, week by week", page);
        Assert.Contains("data-live=\"dashboard\"", page);
        Assert.StartsWith("/Account/AccessDenied", (await staff.GetAsync("/Admin", Cancel)).Headers.Location!.PathAndQuery);
        Assert.DoesNotContain(hub.Code, await chattogramAdmin.GetStringAsync("/Admin", Cancel));
    }

    [Fact]
    public async Task Hub_staff_hear_a_change_to_their_operator_live_and_nobody_else_can_listen()
    {
        WebAppFactory.RequireDatabase();
        var hub = await NewHubAsync();
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var staff = Listen("dhaka", await CookieAsync("dhaka", "hub@dhaka.onedrop.test"));
        staff.On(OperationsHub.Changed, () => changed.TrySetResult());
        await staff.StartAsync(Cancel);

        await CreateAsync(WebAppFactory.DhakaFashion, NewPhone(), hub);

        await changed.Task.WaitAsync(TimeSpan.FromSeconds(15), Cancel);
        await using var anonymous = Listen("dhaka", "");
        await using var merchant = Listen("dhaka", await CookieAsync("dhaka", "gadget@dhaka.onedrop.test"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Assert.ThrowsAsync<HttpRequestException>(() => anonymous.StartAsync(Cancel))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Assert.ThrowsAsync<HttpRequestException>(() => merchant.StartAsync(Cancel))).StatusCode);
    }

    [Fact]
    public async Task Only_a_saved_change_to_operations_tells_the_dashboards_and_only_the_operators_own()
    {
        WebAppFactory.RequireDatabase();
        var feed = new RecordingFeed();
        var dhaka = await TenantAsync("dhaka");
        await using var scope = await ScopeAsync("dhaka");
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(WebAppFactory.ConnectionString)
            .AddInterceptors(scope.ServiceProvider.GetRequiredService<TenantSaveInterceptor>())
            .Options;
        await using var db = new AppDbContext(
            options,
            scope.ServiceProvider.GetRequiredService<ITenantContext>(),
            scope.ServiceProvider.GetRequiredService<ICurrentUser>(),
            feed);

        var code = "D" + Guid.NewGuid().ToString("N")[..7].ToUpperInvariant();
        var hub = new Hub(code, $"Dashboard test hub {code}", "Test road");
        db.Hubs.Add(hub);
        await db.SaveChangesAsync(Cancel);
        var afterHub = feed.Tenants.Count;
        db.Riders.Add(new Rider(hub.Id, "Rider", PhoneNumber.Parse(NewPhone()).Value, new TripLoad(10, 10_000), null));
        await db.SaveChangesAsync(Cancel);
        var afterRider = feed.Tenants.Count;
        await db.SaveChangesAsync(Cancel);

        Assert.Equal(0, afterHub);
        Assert.Equal(1, afterRider);
        Assert.Equal([dhaka.Id], feed.Tenants);
    }

    private async Task<HubNow?> HubNowAsync(string slug, TestHub hub)
    {
        await using var scope = await ScopeAsync(slug);

        return await scope.ServiceProvider.GetRequiredService<OperationsDashboardHandler>().HubAsync(hub.Code, Cancel);
    }

    private async Task<OperatorNow> OperatorNowAsync(string slug)
    {
        await using var scope = await ScopeAsync(slug);

        return await scope.ServiceProvider.GetRequiredService<OperationsDashboardHandler>().OperatorAsync(8, Cancel);
    }

    /// <summary>A SignalR connection to the operator's live line, signed in by <paramref name="cookie"/> (none when empty).</summary>
    private HubConnection Listen(string slug, string cookie)
    {
        return new HubConnectionBuilder()
            .WithUrl(new Uri($"http://{slug}.localhost{OperationsHub.Path}"), HttpTransportType.LongPolling, options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                if (cookie.Length > 0)
                {
                    options.Headers["Cookie"] = cookie;
                }
            })
            .Build();
    }

    /// <summary>Signs a user in without a cookie container and returns the cookies to send, as a Cookie header.</summary>
    private async Task<string> CookieAsync(string slug, string email)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri($"http://{slug}.localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false
        });
        var form = await client.GetAsync("/Account/Login", Cancel);
        var antiforgery = Cookies(form);
        var request = new HttpRequestMessage(HttpMethod.Post, "/Account/Login")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Input.Email"] = email,
                ["Input.Password"] = Password,
                ["__RequestVerificationToken"] = Token().Match(await form.Content.ReadAsStringAsync(Cancel)).Groups[1].Value
            })
        };
        request.Headers.Add("Cookie", antiforgery);
        var signedIn = await client.SendAsync(request, Cancel);
        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);

        return Cookies(signedIn);

        static string Cookies(HttpResponseMessage response)
        {
            return string.Join("; ", response.Headers.GetValues("Set-Cookie").Select(cookie => cookie.Split(';')[0]));
        }
    }

    private sealed class RecordingFeed : IOperationsFeed
    {
        public List<long> Tenants { get; } = [];

        public void Changed(long tenantId)
        {
            Tenants.Add(tenantId);
        }
    }
}
