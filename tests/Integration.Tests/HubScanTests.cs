using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Application.Abstractions;
using Application.Network.HubScan;
using Application.Network.PickupRoutes;
using Domain.Grouping;
using Domain.Merchants;
using Domain.Orders;
using Infrastructure.MultiTenancy;
using Infrastructure.Persistence;

namespace Integration.Tests;

/// <summary>
/// Task 3.2: collecting at the shop, scanning in at the hub and shelving each delivery. The customers here live in
/// Uttara, so their deliveries leave from the Uttara hub, where no other test class shelves anything.
/// </summary>
[Collection("Uttara pickups")]
public partial class HubScanTests(WebAppFactory factory)
{
    private const string Password = "OneDrop#2026";

    [Fact]
    public async Task Three_shops_parcels_scanned_in_at_the_delivery_hub_share_one_shelf()
    {
        WebAppFactory.RequireDatabase();
        var phone = NewPhone();
        var fashion = await CreateAsync(WebAppFactory.DhakaFashion, phone, packages: 2);
        var gadget = await CreateAsync(WebAppFactory.DhakaGadget, phone);
        var beauty = await CreateAsync(WebAppFactory.DhakaBeauty, phone);
        var neighbour = await CreateAsync(WebAppFactory.DhakaFashion, NewPhone());

        var first = await ReceiveAsync("UTT", $"{fashion}-1");
        var scans = new[]
        {
            first,
            await ReceiveAsync("UTT", $" {fashion.ToLowerInvariant()}-2 "),
            await ReceiveAsync("UTT", $"{gadget}-1"),
            await ReceiveAsync("UTT", $"{beauty}-1")
        };
        var again = await ReceiveAsync("UTT", $"{gadget}-1");
        var other = await ReceiveAsync("UTT", $"{neighbour}-1");

        Assert.All(scans, scan => Assert.True(scan.IsSuccess, scan.Error?.Message));
        var shelf = first.Value.Shelf;
        Assert.Matches("^UTT-[0-9]{2,}$", shelf);
        Assert.All(scans, scan => Assert.Equal(shelf, scan.Value.Shelf));
        Assert.All(scans, scan => Assert.Null(scan.Value.SendTo));
        Assert.Equal((1, 2, OrderStatus.PickedUp, false), (first.Value.PackagesIn, first.Value.Packages, first.Value.Status, first.Value.AlreadyScanned));
        Assert.Equal((2, OrderStatus.AtHub), (scans[1].Value.PackagesIn, scans[1].Value.Status));
        Assert.Equal(("Gadget BD", DeliveryGroupStatus.Open), (scans[2].Value.Merchant, scans[2].Value.DeliveryStatus));
        Assert.True(again.Value.AlreadyScanned);
        Assert.Equal(shelf, again.Value.Shelf);
        Assert.NotEqual(shelf, other.Value.Shelf);
        Assert.Equal(
            [OrderStatus.AtHub, OrderStatus.AtHub, OrderStatus.AtHub],
            await StatusesAsync("dhaka", fashion, gadget, beauty));

        var row = (await ShelvesAsync("dhaka", "UTT"))!.Single(r => r.Shelf == shelf);
        Assert.Equal((first.Value.Delivery, 3, 4, 4, false), (row.Delivery, row.Orders, row.PackagesHere, row.Packages, row.Ready));
    }

    [Fact]
    public async Task A_parcel_scanned_in_at_another_hub_is_sent_on_and_shelved_where_its_delivery_leaves_from()
    {
        WebAppFactory.RequireDatabase();
        var order = await CreateAsync(WebAppFactory.DhakaFashion, NewPhone(), area: "Gulshan 1");

        var atUttara = await ReceiveAsync("UTT", $"{order}-1");
        var group = await GroupOfAsync(order);
        var atGulshan = await ReceiveAsync("GUL", $"{order}-1");

        Assert.Equal(("GUL", null, OrderStatus.AtHub), (atUttara.Value.SendTo, atUttara.Value.Shelf, atUttara.Value.Status));
        Assert.Null(group.Shelf);
        Assert.Null(atGulshan.Value.SendTo);
        Assert.False(atGulshan.Value.AlreadyScanned);
        Assert.StartsWith("GUL-", atGulshan.Value.Shelf);
    }

    [Fact]
    public async Task The_lowest_free_shelf_goes_to_the_next_delivery_and_a_dispatched_one_frees_its_shelf()
    {
        WebAppFactory.RequireDatabase();
        var leaving = await CreateAsync(WebAppFactory.DhakaGadget, NewPhone());
        var next = await CreateAsync(WebAppFactory.DhakaGadget, NewPhone());

        var shelf = (await ReceiveAsync("UTT", $"{leaving}-1")).Value.Shelf;
        await using (var scope = await ScopeAsync("dhaka"))
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var groupId = (await db.Orders.SingleAsync(o => o.Number == leaving, Cancel)).DeliveryGroupId;
            var group = await db.DeliveryGroups.SingleAsync(g => g.Id == groupId, Cancel);
            Assert.True(group.ShipNow(DateTime.UtcNow, TimeZoneInfo.Utc).IsSuccess);
            Assert.True(group.MoveTo(DeliveryGroupStatus.Dispatched, DateTime.UtcNow).IsSuccess);
            await db.SaveChangesAsync(Cancel);
        }

        var taken = (await ReceiveAsync("UTT", $"{next}-1")).Value.Shelf;
        var left = await GroupOfAsync(leaving);

        Assert.Equal(shelf, taken);
        Assert.Null(left.Shelf);
        Assert.DoesNotContain((await ShelvesAsync("dhaka", "UTT"))!, row => row.Delivery == left.Number);
    }

    [Fact]
    public async Task Parallel_scans_give_each_delivery_its_own_shelf_and_lose_no_package()
    {
        WebAppFactory.RequireDatabase();
        var orders = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            orders.Add(await CreateAsync(WebAppFactory.DhakaBeauty, NewPhone()));
        }

        var twoPackages = await CreateAsync(WebAppFactory.DhakaFashion, NewPhone(), packages: 2);

        var shelves = await Task.WhenAll(orders.Select(order => ReceiveAsync("UTT", $"{order}-1")));
        var packages = await Task.WhenAll(ReceiveAsync("UTT", $"{twoPackages}-1"), ReceiveAsync("UTT", $"{twoPackages}-2"));

        Assert.All(shelves, scan => Assert.True(scan.IsSuccess));
        Assert.Equal(4, shelves.Select(scan => scan.Value.Shelf).Distinct().Count());
        Assert.All(packages, scan => Assert.False(scan.Value.AlreadyScanned));
        Assert.Equal([OrderStatus.AtHub], await StatusesAsync("dhaka", twoPackages));
    }

    [Fact]
    public async Task Collecting_at_the_shop_takes_the_order_off_the_route_sheet()
    {
        WebAppFactory.RequireDatabase();
        var shop = await NewShopAsync("Uttara Sector 13");
        var order = await CreateAsync(shop.ApiKey, NewPhone(), packages: 2);
        var cancelled = await CreateAsync(shop.ApiKey, NewPhone());
        await using (var scope = await ScopeAsync("dhaka"))
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Orders.SingleAsync(o => o.Number == cancelled, Cancel)).MoveTo(OrderStatus.Cancelled);
            await db.SaveChangesAsync(Cancel);
        }

        var collected = await CollectAsync($"{order}-2");
        var again = await CollectAsync($"{order}-1");
        var refused = await CollectAsync($"{cancelled}-1");
        var sheet = await SheetAsync("Uttara");

        Assert.Equal((OrderStatus.PickedUp, false, 2, shop.Name), (collected.Value.Status, collected.Value.AlreadyScanned, collected.Value.Packages, collected.Value.Merchant));
        Assert.True(again.Value.AlreadyScanned);
        Assert.Equal("order.scan.collect", refused.Error!.Code);
        Assert.DoesNotContain(sheet.Stops, stop => stop.Merchant == shop.Name);
        Assert.Equal([OrderStatus.PickedUp, OrderStatus.Cancelled], await StatusesAsync("dhaka", order, cancelled));
    }

    [Fact]
    public async Task Another_operators_labels_and_hubs_are_not_found_and_junk_is_not_a_label()
    {
        WebAppFactory.RequireDatabase();
        var order = await CreateAsync(WebAppFactory.DhakaFashion, NewPhone(), packages: 2);

        var atAgrabad = await ReceiveAsync("AGR", $"{order}-1", "chattogram");
        var dhakaHub = await ReceiveAsync("UTT", $"{order}-1", "chattogram");
        var collected = await CollectAsync($"{order}-1", "chattogram");
        var junk = await ReceiveAsync("UTT", "DG-100001");
        var noSuchPackage = await ReceiveAsync("UTT", $"{order}-3");

        Assert.Equal("hubScan.label.unknown", atAgrabad.Error!.Code);
        Assert.Equal("hubScan.hub.unknown", dhakaHub.Error!.Code);
        Assert.Equal("hubScan.label.unknown", collected.Error!.Code);
        Assert.Equal("hubScan.label.unreadable", junk.Error!.Code);
        Assert.Equal("hubScan.label.unknown", noSuchPackage.Error!.Code);
        Assert.Null(await ShelvesAsync("chattogram", "UTT"));
        Assert.Equal([OrderStatus.Created], await StatusesAsync("dhaka", order));
    }

    [Fact]
    public async Task Hub_staff_scan_on_the_page_and_see_the_shelves_and_nobody_else_can()
    {
        WebAppFactory.RequireDatabase();
        var order = await CreateAsync(WebAppFactory.DhakaGadget, NewPhone());
        var hub = await StaffSignInAsync("dhaka", "hub@dhaka.onedrop.test");
        var chattogramHub = await StaffSignInAsync("chattogram", "hub@chattogram.onedrop.test");
        var merchant = await StaffSignInAsync("dhaka", "gadget@dhaka.onedrop.test");

        var picker = await hub.GetStringAsync("/Hub/Scan", Cancel);
        var scanned = await PostScanAsync(hub, "/Hub/Scan?hub=UTT", $"{order}-1");
        var group = await GroupOfAsync(order);
        var shelves = await hub.GetStringAsync("/Hub/Shelves?hub=UTT", Cancel);

        Assert.Contains("Uttara hub", picker);
        Assert.Contains("/Hub/Scan?hub=UTT", picker);
        Assert.Contains(DeliveryGroup.ShelfCode("UTT", group.Shelf!.Value), scanned);
        Assert.Contains("Gadget BD", scanned);
        Assert.Contains(DeliveryGroup.ShelfCode("UTT", group.Shelf.Value), shelves);
        Assert.Contains(group.Number, shelves);
        Assert.Contains("is not a parcel label", await PostScanAsync(hub, "/Hub/Scan?hub=UTT&mode=Collect", "hello"));
        Assert.Equal(HttpStatusCode.NotFound, (await chattogramHub.GetAsync("/Hub/Scan?hub=UTT", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await chattogramHub.GetAsync("/Hub/Shelves?hub=UTT", Cancel)).StatusCode);
        var forMerchant = await merchant.GetAsync("/Hub/Scan?hub=UTT", Cancel);
        Assert.StartsWith("/Account/AccessDenied", forMerchant.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task A_parcel_rides_the_shuttle_to_the_hub_its_delivery_leaves_from()
    {
        WebAppFactory.RequireDatabase();
        var order = await CreateAsync(WebAppFactory.DhakaBeauty, NewPhone(), area: "Gulshan 1");
        var label = $"{order}-1";
        await ReceiveAsync("UTT", label);

        var before = (await ShuttleAsync("dhaka", "UTT"))!;
        var loaded = await LoadAsync("UTT", label);
        var again = await LoadAsync("UTT", label);
        var afterLoad = (await ShuttleAsync("dhaka", "UTT"))!;
        var comingToGulshan = (await ShuttleAsync("dhaka", "GUL"))!;
        var arrived = await ReceiveAsync("GUL", label);
        var afterArrival = (await ShuttleAsync("dhaka", "GUL"))!;

        var toGulshan = before.ToLoad.Single(load => load.Hub == "GUL");
        Assert.Equal("Gulshan hub", toGulshan.HubName);
        Assert.Contains(toGulshan.Parcels, parcel => parcel.Label == label && parcel.Merchant == "Beauty Shop");
        Assert.Equal(("GUL", null, false), (loaded.Value.SendTo, loaded.Value.Shelf, loaded.Value.AlreadyScanned));
        Assert.Equal((1, 1), (loaded.Value.PackagesIn, loaded.Value.Packages));
        Assert.True(again.Value.AlreadyScanned);
        Assert.DoesNotContain(afterLoad.ToLoad.SelectMany(load => load.Parcels), parcel => parcel.Label == label);
        Assert.Contains(comingToGulshan.OnTheWay, parcel => parcel.Label == label);
        Assert.StartsWith("GUL-", arrived.Value.Shelf);
        Assert.DoesNotContain(afterArrival.OnTheWay, parcel => parcel.Label == label);
        Assert.Equal([OrderStatus.AtHub], await StatusesAsync("dhaka", order));
    }

    [Fact]
    public async Task The_shuttle_takes_only_parcels_here_for_another_hub_next_day_ones_first()
    {
        WebAppFactory.RequireDatabase();
        var phone = NewPhone();
        var waiting = await CreateAsync(WebAppFactory.DhakaFashion, phone, area: "Gulshan 2");
        var fast = await CreateAsync(WebAppFactory.DhakaGadget, phone, area: "Gulshan 2", speed: "fast");
        var cancelled = await CreateAsync(WebAppFactory.DhakaBeauty, NewPhone(), area: "Gulshan 2");
        var home = await CreateAsync(WebAppFactory.DhakaBeauty, NewPhone());
        var notScanned = await CreateAsync(WebAppFactory.DhakaGadget, NewPhone(), area: "Gulshan 2");
        foreach (var number in new[] { waiting, fast, cancelled, home })
        {
            await ReceiveAsync("UTT", $"{number}-1");
        }

        await using (var scope = await ScopeAsync("dhaka"))
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Orders.SingleAsync(o => o.Number == cancelled, Cancel)).MoveTo(OrderStatus.Cancelled);
            await db.SaveChangesAsync(Cancel);
        }

        var manifest = (await ShuttleAsync("dhaka", "UTT"))!;
        var labels = manifest.ToLoad.Single(load => load.Hub == "GUL").Parcels.Select(parcel => parcel.Label).ToList();

        Assert.True(labels.IndexOf($"{fast}-1") < labels.IndexOf($"{waiting}-1"));
        Assert.DoesNotContain($"{cancelled}-1", labels);
        Assert.DoesNotContain(manifest.ToLoad.SelectMany(load => load.Parcels), parcel => parcel.Label == $"{home}-1");
        Assert.Equal("order.scan.shuttle.home", (await LoadAsync("UTT", $"{home}-1")).Error!.Code);
        Assert.Equal("order.scan.shuttle", (await LoadAsync("UTT", $"{notScanned}-1")).Error!.Code);
        Assert.Equal("order.scan.notHere", (await LoadAsync("GUL", $"{waiting}-1")).Error!.Code);
        Assert.Equal("order.scan.shuttle", (await LoadAsync("UTT", $"{cancelled}-1")).Error!.Code);
        Assert.Equal("hubScan.label.unknown", (await LoadAsync("AGR", $"{waiting}-1", "chattogram")).Error!.Code);
        Assert.Null(await ShuttleAsync("chattogram", "UTT"));
    }

    [Fact]
    public async Task Hub_staff_load_the_shuttle_on_the_page_and_nobody_else_sees_the_manifest()
    {
        WebAppFactory.RequireDatabase();
        var order = await CreateAsync(WebAppFactory.DhakaFashion, NewPhone(), area: "Banani");
        await ReceiveAsync("UTT", $"{order}-1");
        var hub = await StaffSignInAsync("dhaka", "hub@dhaka.onedrop.test");
        var chattogramHub = await StaffSignInAsync("chattogram", "hub@chattogram.onedrop.test");

        var manifest = await hub.GetStringAsync("/Hub/Shuttle?hub=UTT", Cancel);
        var loaded = await PostScanAsync(hub, "/Hub/Scan?hub=UTT&mode=Load", $"{order}-1");
        var arriving = await hub.GetStringAsync("/Hub/Shuttle?hub=GUL", Cancel);

        Assert.Contains("To Gulshan hub", manifest);
        Assert.Contains($"{order}-1", manifest);
        Assert.Contains("Load for", loaded);
        Assert.Contains("GUL hub", loaded);
        Assert.Contains($"{order}-1", arriving);
        Assert.Equal(HttpStatusCode.NotFound, (await chattogramHub.GetAsync("/Hub/Shuttle?hub=UTT", Cancel)).StatusCode);
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private async Task<Domain.Common.Result<ParcelScan>> ReceiveAsync(string hub, string label, string slug = "dhaka")
    {
        await using var scope = await ScopeAsync(slug);

        return await scope.ServiceProvider.GetRequiredService<HubScanHandler>().ReceiveAsync(hub, label, Cancel);
    }

    private async Task<Domain.Common.Result<ParcelScan>> LoadAsync(string hub, string label, string slug = "dhaka")
    {
        await using var scope = await ScopeAsync(slug);

        return await scope.ServiceProvider.GetRequiredService<HubScanHandler>().LoadAsync(hub, label, Cancel);
    }

    private async Task<ShuttleManifest?> ShuttleAsync(string slug, string hub)
    {
        await using var scope = await ScopeAsync(slug);

        return await scope.ServiceProvider.GetRequiredService<HubScanHandler>().ShuttleAsync(hub, Cancel);
    }

    private async Task<Domain.Common.Result<ParcelScan>> CollectAsync(string label, string slug = "dhaka")
    {
        await using var scope = await ScopeAsync(slug);

        return await scope.ServiceProvider.GetRequiredService<HubScanHandler>().CollectAsync(label, Cancel);
    }

    private async Task<IReadOnlyList<ShelfRow>?> ShelvesAsync(string slug, string hub)
    {
        await using var scope = await ScopeAsync(slug);

        return await scope.ServiceProvider.GetRequiredService<HubScanHandler>().ShelvesAsync(hub, Cancel);
    }

    private async Task<PickupRouteSheet> SheetAsync(string zone)
    {
        await using var scope = await ScopeAsync("dhaka");
        var handler = scope.ServiceProvider.GetRequiredService<PickupRoutesHandler>();
        var route = (await handler.ListAsync(Cancel)).Single(r => r.Zone == zone);

        return (await handler.GetAsync(route.Id, Cancel))!;
    }

    private async Task<DeliveryGroup> GroupOfAsync(string number)
    {
        await using var scope = await ScopeAsync("dhaka");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.Orders
            .Where(o => o.Number == number)
            .Select(o => o.DeliveryGroup!)
            .AsNoTracking()
            .SingleAsync(Cancel);
    }

    private async Task<OrderStatus[]> StatusesAsync(string slug, params string[] numbers)
    {
        await using var scope = await ScopeAsync(slug);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var statuses = await db.Orders
            .Where(o => numbers.Contains(o.Number))
            .ToDictionaryAsync(o => o.Number, o => o.Status, Cancel);

        return [.. numbers.Select(number => statuses[number])];
    }

    /// <summary>A shop of this test's own, with its default pickup point in <paramref name="area"/>.</summary>
    private async Task<(string Name, string ApiKey)> NewShopAsync(string area)
    {
        await using var scope = await ScopeAsync("dhaka");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var name = $"Scan Shop {Guid.NewGuid():N}"[..19];
        var pickupArea = await db.Areas.SingleAsync(a => a.Name == area, Cancel);
        var merchant = new Merchant(name, pickupArea.ZoneId, "01711999999", null);
        db.Merchants.Add(merchant);
        await db.SaveChangesAsync(Cancel);

        var (key, plaintext) = MerchantApiKey.Issue(merchant.Id, "Scan test");
        db.PickupPoints.Add(new PickupPoint(merchant.Id, pickupArea.Id, "Shop", $"{name}, {area}", "01711999999", isDefault: true));
        db.MerchantApiKeys.Add(key);
        await db.SaveChangesAsync(Cancel);

        return (name, plaintext);
    }

    private async Task<string> CreateAsync(
        string apiKey,
        string phone,
        int packages = 1,
        string area = "Uttara Sector 7",
        string speed = "combine")
    {
        var order = new
        {
            Customer = new { Name = "Scan Customer", Phone = phone },
            Address = new { Area = area, Line1 = "House 12, Road 4" },
            Packages = Enumerable.Range(1, packages).Select(_ => new { Description = "Parcel", WeightGrams = 400 }),
            Speed = speed
        };
        var response = await factory.ClientFor(apiKey).PostAsJsonAsync("/api/v1/orders", order, Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<Created>(Cancel))!.Number;
    }

    private async Task<string> PostScanAsync(HttpClient client, string url, string label)
    {
        var token = Token().Match(await client.GetStringAsync(url, Cancel)).Groups[1].Value;
        var fields = new Dictionary<string, string> { ["Label"] = label, ["__RequestVerificationToken"] = token };
        var response = await client.PostAsync(url, new FormUrlEncodedContent(fields), Cancel);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadAsStringAsync(Cancel);
    }

    /// <summary>Signs a demo staff user in on their operator's subdomain. Keeps the sign-in cookie.</summary>
    private async Task<HttpClient> StaffSignInAsync(string slug, string email)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri($"http://{slug}.localhost"),
            AllowAutoRedirect = false
        });
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

    private async Task<AsyncServiceScope> ScopeAsync(string slug)
    {
        var scope = factory.Services.CreateAsyncScope();
        var tenant = await factory.Services.GetRequiredService<ITenantCatalog>().FindBySlugAsync(slug, Cancel);
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(tenant!);

        return scope;
    }

    private static string NewPhone()
    {
        return "017" + Random.Shared.Next(0, 100_000_000).ToString("D8");
    }

    [GeneratedRegex("""name="__RequestVerificationToken" type="hidden" value="([^"]+)""")]
    private static partial Regex Token();

    private sealed record Created(string Number);
}
