using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.StaticAssets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Application.Abstractions;
using Domain.Customers;
using Domain.Delivery;
using Domain.Grouping;
using Domain.Orders;
using Domain.Payments;
using Infrastructure.Persistence;
using Infrastructure.Sms;

namespace Integration.Tests;

/// <summary>
/// Task 4.3: the tenant isolation sweep. Every page and endpoint the app maps is listed here with what another operator,
/// or another shop of the same operator, gets when it asks for a record that is not its own; a route added without a
/// line here fails the sweep. A sign-in is refused on every route of another operator's host and of the platform's.
/// </summary>
public partial class TripTests
{
    [Fact]
    public async Task Another_operator_or_shop_reaches_nothing_of_a_delivery_on_any_page_or_endpoint()
    {
        WebAppFactory.RequireDatabase();
        var hub = await NewHubAsync();
        var rider = await NewRiderAsync(hub, "Isolation rider", new TripLoad(30, 25_000));
        var otherRider = await NewRiderAsync(hub, "Other rider", new TripLoad(30, 25_000));
        var phone = NewPhone();
        var sent = await CreateAsync(WebAppFactory.DhakaFashion, phone, hub, cod: 700);
        var waiting = await CreateAsync(WebAppFactory.DhakaFashion, phone, hub, cod: 500, line1: "Flat 2B, House 9, Road 1");
        var delivery = await DueAsync(hub, [sent]);
        Assert.Equal(1, (await PlanAsync("dhaka", hub))!.Planned);
        Assert.True((await StartAsync("dhaka", rider.UserId!.Value)).IsSuccess);
        var dhaka = await TenantAsync("dhaka");
        var chattogram = await TenantAsync("chattogram");
        long tripId, areaId, pickupPointId, routeId;
        string token, open;
        await using (var scope = await ScopeAsync("dhaka"))
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            tripId = await db.Trips.Where(t => t.RiderId == rider.Rider.Id).Select(t => t.Id).SingleAsync(Cancel);
            areaId = await db.Areas.Where(a => a.Name == hub.Area).Select(a => a.Id).SingleAsync(Cancel);
            pickupPointId = await db.Orders.Where(o => o.Number == sent).Select(o => o.PickupPointId).SingleAsync(Cancel);
            routeId = await db.PickupRoutes.Select(r => r.Id).FirstAsync(Cancel);
            token = (await db.Orders.Where(o => o.Number == waiting).Select(o => o.CustomerToken).SingleAsync(Cancel))!;
            open = await db.Orders.Where(o => o.Number == waiting).Select(o => o.DeliveryGroup!.Number).SingleAsync(Cancel);
        }

        var fashion = Visit("dhaka", await SignInCookieAsync("dhaka", "fashion@dhaka.onedrop.test"));
        var gadget = Visit("dhaka", await SignInCookieAsync("dhaka", "gadget@dhaka.onedrop.test"));
        var dhakaHub = Visit("dhaka", await SignInCookieAsync("dhaka", "hub@dhaka.onedrop.test"));
        var dhakaAdmin = Visit("dhaka", await SignInCookieAsync("dhaka", "admin@dhaka.onedrop.test"));
        var dhakaRider = Visit("dhaka", await SignInCookieAsync("dhaka", rider.Email));
        var sameHubRider = Visit("dhaka", await SignInCookieAsync("dhaka", otherRider.Email));
        var ctgShop = Visit("chattogram", await SignInCookieAsync("chattogram", "fashion@chattogram.onedrop.test"));
        var ctgHub = Visit("chattogram", await SignInCookieAsync("chattogram", "hub@chattogram.onedrop.test"));
        var ctgAdmin = Visit("chattogram", await SignInCookieAsync("chattogram", "admin@chattogram.onedrop.test"));
        var ctgRider = Visit("chattogram", await SignInCookieAsync("chattogram", "rider@chattogram.onedrop.test"));
        var ctgCustomer = Visit("chattogram", await CustomerCookieAsync("chattogram", phone));
        var ctgAnonymous = Visit("chattogram");
        var ctgKey = factory.ClientFor(WebAppFactory.ChattogramFashion);
        var gadgetKey = factory.ClientFor(WebAppFactory.DhakaGadget);
        var hubPage = $"?hub={hub.Code}";

        // Each route: what the operator's own staff or shop see (so the check can fail), then what the others get.
        // A route with nothing to ask for says why.
        (string Route, Func<Task> Check)[] sweep =
        [
            ("/Index", async () =>
                Assert.DoesNotContain(dhaka.Name, await ctgAnonymous.PageAsync("/"))),
            ("/Guide", async () =>
                Assert.DoesNotContain("@dhaka.onedrop.test", await ctgAnonymous.PageAsync("/Guide"))),
            ("/Account/Login", async () =>
                Assert.DoesNotContain("@dhaka.onedrop.test", await ctgAnonymous.PageAsync("/Account/Login"))),
            // Fixed text, no records
            ("/Account/AccessDenied", () => Task.CompletedTask),
            ("/Account/Logout", () => Task.CompletedTask),
            ("/Error", () => Task.CompletedTask),
            ("/Account/PhoneLogin", async () =>
            {
                // The Dhaka customer's phone signed in at Chattogram is a customer of Chattogram's own
                var e164 = PhoneNumber.Parse(phone).Value.Value;
                Assert.NotEqual(await CustomerIdAsync("dhaka", e164), await CustomerIdAsync("chattogram", e164));
            }),
            ("/Customer/Index", async () =>
            {
                var page = await ctgCustomer.PageAsync("/Customer");
                Assert.DoesNotContain(sent, page);
                Assert.DoesNotContain(open, page);
                await ctgCustomer.PostFormAsync("/Customer", "/Customer?handler=ShipNow", ("number", open));
            }),
            ("POST /api/v1/deliveries/{number}/ship-now", async () =>
                Assert.Equal(
                    HttpStatusCode.NotFound,
                    (await ctgCustomer.SendAsync(HttpMethod.Post, $"/api/v1/deliveries/{open}/ship-now")).StatusCode)),
            ("/Customer/Order", async () =>
            {
                Assert.Equal(HttpStatusCode.OK, (await Visit("dhaka").GetAsync($"/Customer/Order?token={token}")).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await ctgAnonymous.GetAsync($"/Customer/Order?token={token}")).StatusCode);
                foreach (var handler in new[] { "Confirm", "Pay", "Check" })
                {
                    var posted = await ctgAnonymous.PostFormAsync(
                        "/Account/Login",
                        $"/Customer/Order?token={token}&handler={handler}",
                        ("method", nameof(PaymentMethod.Bkash)));
                    Assert.Equal(HttpStatusCode.NotFound, posted.StatusCode);
                }
            }),
            // Every operator's fake SMS, wallet and shop websites at once: only in Development (see the next test)
            ("/Dev/Payments", () => Task.CompletedTask),
            ("/Dev/Sms", () => Task.CompletedTask),
            ("/Dev/Webhooks", () => Task.CompletedTask),
            ("/Admin/Index", async () =>
            {
                Assert.Contains(hub.Code, await dhakaAdmin.PageAsync("/Admin"));
                Assert.DoesNotContain(hub.Code, await ctgAdmin.PageAsync("/Admin"));
            }),
            ("/Platform/Tenants", async () =>
            {
                var response = await dhakaAdmin.GetAsync("/Platform/Tenants");
                Assert.StartsWith("/Account/AccessDenied", response.Headers.Location!.PathAndQuery);
            }),
            ("/jobs/{**path}", async () =>
                Assert.NotEqual(HttpStatusCode.OK, (await dhakaAdmin.GetAsync("/jobs")).StatusCode)),
            // The connection joins its operator's group from its sign-in: there is nothing to ask for.
            // Hearing only its own operator is tested by Only_a_saved_change_to_operations_tells_the_dashboards_and_only_the_operators_own
            ("/hubs/operations", () => Task.CompletedTask),
            ("/Hub/Index", () => NotFoundForOthersAsync("/Hub" + hubPage)),
            ("/Hub/Shelves", () => NotFoundForOthersAsync("/Hub/Shelves" + hubPage)),
            ("/Hub/Shuttle", () => NotFoundForOthersAsync("/Hub/Shuttle" + hubPage)),
            ("/Hub/Trips", async () =>
            {
                await NotFoundForOthersAsync("/Hub/Trips" + hubPage);
                var planned = await ctgHub.PostFormAsync("/Hub/Trips", $"/Hub/Trips{hubPage}&handler=Plan");
                Assert.Equal(HttpStatusCode.NotFound, planned.StatusCode);
            }),
            ("/Hub/Cash", async () =>
            {
                await NotFoundForOthersAsync("/Hub/Cash" + hubPage);
                (string, string)[] handIn = [("trip", tripId.ToString()), ("received", "1")];
                // Another operator at its own hub or at this one, and the same operator's staff at another hub
                var elsewhere = await ctgHub.PostFormAsync("/Hub/Cash", "/Hub/Cash?hub=AGR&handler=HandIn", handIn);
                var here = await ctgHub.PostFormAsync("/Hub/Cash", $"/Hub/Cash{hubPage}&handler=HandIn", handIn);
                var otherHub = await dhakaHub.PostFormAsync("/Hub/Cash", "/Hub/Cash?hub=MIR&handler=HandIn", handIn);
                Assert.Equal(
                    [HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound],
                    new[] { elsewhere.StatusCode, here.StatusCode, otherHub.StatusCode });
            }),
            ("/Hub/Scan", async () =>
            {
                await NotFoundForOthersAsync("/Hub/Scan" + hubPage);
                foreach (var mode in new[] { "Collect", "Receive", "Load", "Return" })
                {
                    var scanned = await ctgHub.PostFormAsync(
                        "/Hub/Scan?hub=AGR",
                        $"/Hub/Scan?hub=AGR&mode={mode}",
                        ("Label", $"{waiting}-1"));
                    Assert.Equal(HttpStatusCode.OK, scanned.StatusCode);
                }
            }),
            ("/Hub/Routes", async () =>
            {
                var zone = await ZoneOfRouteAsync(routeId);
                Assert.Contains(zone, await dhakaHub.PageAsync("/Hub/Routes"));
                Assert.DoesNotContain(zone, await ctgHub.PageAsync("/Hub/Routes"));
            }),
            ("/Hub/RouteSheet", async () =>
            {
                Assert.Equal(HttpStatusCode.OK, (await dhakaHub.GetAsync($"/Hub/RouteSheet/{routeId}")).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await ctgHub.GetAsync($"/Hub/RouteSheet/{routeId}")).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await ctgAdmin.GetAsync($"/Hub/RouteSheet/{routeId}")).StatusCode);
            }),
            ("/Rider/Index", async () =>
            {
                Assert.Contains($"{sent}-1", await dhakaRider.PageAsync($"/Rider?stop={delivery}"));
                foreach (var stranger in new[] { ctgRider, sameHubRider })
                {
                    var page = await stranger.PageAsync($"/Rider?stop={delivery}");
                    Assert.DoesNotContain($"{sent}-1", page);
                    Assert.DoesNotContain("Trip Customer", page);
                    await stranger.PostFormAsync("/Rider", "/Rider?handler=NotHome", ("stop", delivery));
                    await stranger.PostFormAsync(
                        "/Rider",
                        "/Rider?handler=HandOver",
                        ("stop", delivery),
                        ("collected", "0"),
                        ("method", nameof(PaymentMethod.Cash)));
                }

                Assert.Null(await StopOutcomeAsync(tripId));
                Assert.Equal([OrderStatus.OutForDelivery], await StatusesAsync(sent));
            }),
            ("/Merchant/Orders", () => OnlyTheShopSeesAsync("/Merchant/Orders", sent)),
            ("/Merchant/Labels", () => OnlyTheShopSeesAsync($"/Merchant/Labels?order={sent}", $"{sent}-1")),
            ("/Merchant/NewOrder", async () =>
            {
                var posted = await ctgShop.PostFormAsync(
                    "/Merchant/NewOrder",
                    "/Merchant/NewOrder",
                    ("Form.Key", Guid.NewGuid().ToString("N")),
                    ("Form.Name", "Isolation Customer"),
                    ("Form.Phone", phone),
                    ("Form.AreaId", areaId.ToString()),
                    ("Form.Line1", "House 12, Road 4"),
                    ("Form.Description", "Parcel"),
                    ("Form.Parcels", "1"),
                    ("Form.WeightKg", "0.5"),
                    ("Form.Cod", "100"));
                Assert.Contains("The order was not saved", WebUtility.HtmlDecode(await posted.Content.ReadAsStringAsync(Cancel)));
            }),
            ("/Merchant/Webhook", async () =>
            {
                foreach (var (shop, tenant, name) in new[] { (gadget, dhaka.Id, "Gadget BD"), (ctgShop, chattogram.Id, "Fashion House") })
                {
                    var page = await shop.PageAsync("/Merchant/Webhook");
                    foreach (var secret in await WebhookSecretsExceptAsync(tenant, name))
                    {
                        Assert.DoesNotContain(secret, page);
                    }
                }
            }),
            ("/Merchant/Window", async () =>
            {
                var url = $"https://isolation-{Guid.NewGuid():N}.example/";
                await ListFashionHouseAsync(url);
                try
                {
                    Assert.Contains(url, await fashion.PageAsync("/Merchant/Window"));
                    Assert.DoesNotContain(url, await gadget.PageAsync("/Merchant/Window"));
                    Assert.DoesNotContain(url, await ctgShop.PageAsync("/Merchant/Window"));
                }
                finally
                {
                    await ListFashionHouseAsync(null);
                }
            }),
            ("/Shops", async () =>
            {
                // Open to everyone, so it may show a listed shop to anyone, but only its own operator's
                var url = $"https://isolation-{Guid.NewGuid():N}.example/";
                await ListFashionHouseAsync(url);
                try
                {
                    Assert.Contains(url, await Visit("dhaka").PageAsync("/Shops"));
                    Assert.DoesNotContain(url, await ctgAnonymous.PageAsync("/Shops"));
                }
                finally
                {
                    await ListFashionHouseAsync(null);
                }
            }),
            ("GET /api/v1/areas", async () =>
                Assert.DoesNotContain(hub.Area, await ctgKey.GetStringAsync("/api/v1/areas", Cancel))),
            ("GET /api/v1/orders/{number}", async () =>
            {
                Assert.Equal(HttpStatusCode.OK, (await factory.ClientFor(WebAppFactory.DhakaFashion).GetAsync($"/api/v1/orders/{sent}", Cancel)).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await ctgKey.GetAsync($"/api/v1/orders/{sent}", Cancel)).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await gadgetKey.GetAsync($"/api/v1/orders/{sent}", Cancel)).StatusCode);
            }),
            ("POST /api/v1/orders", async () =>
            {
                var intoDhaka = await ctgKey.PostAsJsonAsync("/api/v1/orders", Order(new { AreaId = areaId, Line1 = "House 1" }, null), Cancel);
                var fromFashion = await gadgetKey.PostAsJsonAsync("/api/v1/orders", Order(new { Area = hub.Area, Line1 = "House 1" }, pickupPointId), Cancel);
                Assert.Contains("order.area.unknown", await intoDhaka.Content.ReadAsStringAsync(Cancel));
                Assert.Contains("order.pickupPoint.unknown", await fromFashion.Content.ReadAsStringAsync(Cancel));
            }),
            ("GET /api/v1/quote", async () =>
            {
                var intoDhaka = await ctgKey.GetAsync($"/api/v1/quote?phone={phone}&areaId={areaId}&line1=House%201", Cancel);
                var fromFashion = await gadgetKey.GetAsync(
                    $"/api/v1/quote?phone={phone}&area={Uri.EscapeDataString(hub.Area)}&line1=House%201&pickupPointId={pickupPointId}",
                    Cancel);
                Assert.Contains("quote.area.unknown", await intoDhaka.Content.ReadAsStringAsync(Cancel));
                Assert.Contains("quote.pickupPoint.unknown", await fromFashion.Content.ReadAsStringAsync(Cancel));
            }),
            // Last: hands the delivery over so the shop has a COD line to see
            ("/Merchant/Payouts", async () =>
            {
                var userId = rider.UserId!.Value;
                var due = (await DoorAsync("dhaka", door => door.DueAsync(userId, delivery, [], Cancel))).Value;
                Assert.True((await DoorAsync("dhaka", door => door.HandOverAsync(userId, delivery, [], due.Total, PaymentMethod.Cash, Cancel))).IsSuccess);
                await OnlyTheShopSeesAsync("/Merchant/Payouts", sent);
            })
        ];

        var mapped = Routes().Select(route => route.Key).ToArray();
        var swept = sweep.Select(line => line.Route).ToArray();
        Assert.True(
            mapped.Order().SequenceEqual(swept.Order()),
            $"Add each new route to the sweep with what another operator or shop gets there: {string.Join(", ", mapped.Except(swept))}. " +
            $"Swept but no longer mapped: {string.Join(", ", swept.Except(mapped))}.");
        var failures = new List<string>();
        foreach (var (route, check) in sweep)
        {
            try
            {
                await check();
            }
            catch (Exception exception)
            {
                failures.Add($"{route}: {exception.Message}");
            }
        }

        Assert.Empty(failures);
        await using (var scope = await ScopeAsync("dhaka"))
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var untouched = await db.Orders.Include(o => o.Packages).SingleAsync(o => o.Number == waiting, Cancel);
            Assert.Equal((OrderStatus.Created, null), (untouched.Status, untouched.ConfirmedOn));
            Assert.All(untouched.Packages, package => Assert.Equal((null, null), (package.HubId, package.ShuttleToHubId)));
            Assert.Equal(DeliveryGroupStatus.Open, await db.DeliveryGroups.Where(g => g.Number == open).Select(g => g.Status).SingleAsync(Cancel));
            Assert.Null(await db.Trips.Where(t => t.Id == tripId).Select(t => t.CashReceived).SingleAsync(Cancel));
        }

        // Nothing was ordered in the customer's name by another operator or shop
        Assert.Equal(2, await OrderCountAsync("dhaka", phone));
        Assert.Equal(0, await OrderCountAsync("chattogram", phone));

        // The operator's own staff open the hub's pages; another operator's staff and admin get a 404
        async Task NotFoundForOthersAsync(string url)
        {
            Assert.Equal(HttpStatusCode.OK, (await dhakaHub.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await ctgHub.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await ctgAdmin.GetAsync(url)).StatusCode);
        }

        // Fashion House sees its order on the page; Gadget BD and Chattogram's Fashion House do not
        async Task OnlyTheShopSeesAsync(string url, string text)
        {
            Assert.Contains(text, await fashion.PageAsync(url));
            Assert.DoesNotContain(text, await gadget.PageAsync(url));
            Assert.DoesNotContain(text, await ctgShop.PageAsync(url));
        }

        object Order(object address, long? pickupPoint)
        {
            return new
            {
                Customer = new { Name = "Isolation Customer", Phone = phone },
                Address = address,
                Packages = new[] { new { Description = "Parcel", WeightGrams = 400 } },
                CodAmount = 100,
                PickupPointId = pickupPoint
            };
        }
    }

    [Fact]
    public async Task A_sign_in_is_refused_on_every_route_of_another_operator_and_of_the_platform()
    {
        WebAppFactory.RequireDatabase();
        (string Who, string Home, string Cookie)[] signIns =
        [
            ("shop", "dhaka", await SignInCookieAsync("dhaka", "gadget@dhaka.onedrop.test")),
            ("hub staff", "dhaka", await SignInCookieAsync("dhaka", "hub@dhaka.onedrop.test")),
            ("operator admin", "dhaka", await SignInCookieAsync("dhaka", "admin@dhaka.onedrop.test")),
            ("rider", "dhaka", await SignInCookieAsync("dhaka", "rider@dhaka.onedrop.test")),
            ("customer", "dhaka", await CustomerCookieAsync("dhaka", NewPhone())),
            ("platform admin", "", await SignInCookieAsync("", "admin@onedrop.test"))
        ];
        var routes = Routes();

        var let = new List<string>();
        foreach (var (who, home, cookie) in signIns)
        {
            Assert.NotEqual(HttpStatusCode.Forbidden, (await Visit(home, cookie).GetAsync("/")).StatusCode);
            foreach (var host in new[] { "dhaka", "chattogram", "" }.Where(host => host != home))
            {
                foreach (var route in routes)
                {
                    var response = await Visit(host, cookie).SendAsync(route.Method, route.Url);
                    if (response.StatusCode != HttpStatusCode.Forbidden)
                    {
                        let.Add($"{who} of '{home}' on '{host}': {route.Method} {route.Url} answered {(int)response.StatusCode}");
                    }
                }
            }
        }

        Assert.Empty(let);
    }

    [Fact]
    public async Task The_pages_that_show_every_operator_are_not_there_outside_development()
    {
        WebAppFactory.RequireDatabase();
        await using var production = factory.WithWebHostBuilder(builder => builder
            .UseEnvironment("Production")
            .UseSetting("Links:PortalUrlFormat", "http://{slug}.localhost/"));
        var client = production.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://dhaka.localhost"),
            AllowAutoRedirect = false
        });

        foreach (var url in new[] { "/Dev/Sms", "/Dev/Payments", "/Dev/Webhooks" })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(url, Cancel)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/Dev/Webhooks", new StringContent("{}"), Cancel)).StatusCode);
        Assert.DoesNotContain("onedrop.test", await client.GetStringAsync("/Account/Login", Cancel));
    }

    /// <summary>
    /// Every page, API action, SignalR hub and dashboard the app maps, once each, with a URL that reaches it (any id
    /// is 1). Static files are left out: they hold no records.
    /// </summary>
    private IReadOnlyList<AppRoute> Routes()
    {
        return
        [
            .. factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
                .OfType<RouteEndpoint>()
                .Select(RouteOf)
                .OfType<AppRoute>()
                .GroupBy(route => route.Key)
                .Select(same => same.MinBy(route => route.Url.Length)!)
                .OrderBy(route => route.Key, StringComparer.Ordinal)
        ];

        static AppRoute? RouteOf(RouteEndpoint endpoint)
        {
            var pattern = "/" + endpoint.RoutePattern.RawText!.TrimStart('/');
            var url = Parameter().Replace(pattern, "1");
            var method = new HttpMethod(endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.FirstOrDefault() ?? "GET");
            if (endpoint.Metadata.GetMetadata<PageActionDescriptor>() is { } page)
            {
                return new AppRoute(page.ViewEnginePath, HttpMethod.Get, url);
            }

            if (endpoint.Metadata.GetMetadata<ControllerActionDescriptor>() is not null)
            {
                return new AppRoute($"{method} {pattern}", method, url);
            }

            if (endpoint.Metadata.GetMetadata<HubMetadata>() is not null)
            {
                return pattern.EndsWith("/negotiate", StringComparison.Ordinal)
                    ? new AppRoute(pattern[..^"/negotiate".Length], HttpMethod.Post, url + "?negotiateVersion=1")
                    : null;
            }

            // wwwroot's files, and the fallback that serves a file added while the app runs (a path ending in a file name)
            if (endpoint.Metadata.GetMetadata<StaticAssetDescriptor>() is not null ||
                pattern.EndsWith(":file}", StringComparison.Ordinal))
            {
                return null;
            }

            // Anything else (the jobs dashboard, a minimal API endpoint) by its pattern, so it cannot slip past
            return new AppRoute(pattern, method, url);
        }
    }

    /// <summary>A visitor to one host, with <paramref name="cookie"/> as its sign-in (none when empty).</summary>
    private Visitor Visit(string slug, string cookie = "", string? address = null)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(slug.Length == 0 ? "http://localhost" : $"http://{slug}.localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false
        });
        if (address is not null)
        {
            client.DefaultRequestHeaders.Add(ClientAddressFilter.Header, address);
        }

        return new Visitor(client, cookie);
    }

    /// <summary>Signs a staff user in on <paramref name="slug"/>'s host ("" for the platform) and returns the cookies.</summary>
    private async Task<string> SignInCookieAsync(string slug, string email)
    {
        var signedIn = await Visit(slug).PostFormAsync(
            "/Account/Login",
            "/Account/Login",
            ("Input.Email", email),
            ("Input.Password", Password));
        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);

        return Visitor.Cookies(signedIn);
    }

    /// <summary>
    /// Signs a customer in with the code texted to <paramref name="phone"/> and returns the cookies. The requests come
    /// from an address of their own, so they do not use up the phone sign-in's allowance other tests share.
    /// </summary>
    private async Task<string> CustomerCookieAsync(string slug, string phone)
    {
        var visitor = Visit(slug, address: $"10.43.{Random.Shared.Next(256)}.{Random.Shared.Next(1, 255)}");
        var sent = await visitor.PostFormAsync("/Account/Login", "/Account/PhoneLogin?handler=Send", ("Phone", phone));
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        var e164 = PhoneNumber.Parse(phone).Value.Value;
        var text = factory.Services.GetRequiredService<SmsLog>().Recent.First(message => message.To == e164).Text;
        var verified = await visitor.PostFormAsync(
            "/Account/Login",
            "/Account/PhoneLogin?handler=Verify",
            ("Phone", phone),
            ("Code", SixDigits().Match(text).Value));
        Assert.Equal(HttpStatusCode.Redirect, verified.StatusCode);

        return Visitor.Cookies(verified);
    }

    private async Task<long> CustomerIdAsync(string slug, string e164)
    {
        await using var scope = await ScopeAsync(slug);

        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Customers
            .Where(c => c.Phone == e164)
            .Select(c => c.Id)
            .SingleAsync(Cancel);
    }

    private async Task<int> OrderCountAsync(string slug, string phone)
    {
        await using var scope = await ScopeAsync(slug);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var e164 = PhoneNumber.Parse(phone).Value.Value;

        return await db.Orders.CountAsync(o => db.Customers.Any(c => c.Id == o.CustomerId && c.Phone == e164), Cancel);
    }

    private async Task<string> ZoneOfRouteAsync(long routeId)
    {
        await using var scope = await ScopeAsync("dhaka");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.PickupRoutes
            .Where(r => r.Id == routeId)
            .Select(r => db.Zones.Single(z => z.Id == r.ZoneId).Name)
            .SingleAsync(Cancel);
    }

    private async Task<StopOutcome?> StopOutcomeAsync(long tripId)
    {
        await using var scope = await ScopeAsync("dhaka");

        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().TripStops
            .Where(s => s.TripId == tripId)
            .Select(s => s.Outcome)
            .SingleAsync(Cancel);
    }

    /// <summary>The webhook secrets of every shop of every operator but the one named.</summary>
    private async Task<string[]> WebhookSecretsExceptAsync(long tenantId, string name)
    {
        await using var scope = await ScopeAsync("dhaka");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var database = await db.AcrossTenantsAsync(Cancel);

        return await db.Merchants
            .IgnoreQueryFilters([QueryFilters.Tenant, QueryFilters.Merchant])
            .Where(m => m.WebhookSecret != null && !(m.TenantId == tenantId && m.Name == name))
            .Select(m => m.WebhookSecret!)
            .ToArrayAsync(Cancel);
    }

    /// <summary>Lists Dhaka's Fashion House in the shopping window with the address given, or takes it out.</summary>
    private async Task ListFashionHouseAsync(string? url)
    {
        await using var scope = await ScopeAsync("dhaka");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var merchant = await db.Merchants.SingleAsync(m => m.Name == "Fashion House", Cancel);
        if (url is null)
        {
            merchant.LeaveWindow();
        }
        else
        {
            Assert.True(merchant.ListInWindow(url, null).IsSuccess);
        }

        await db.SaveChangesAsync(Cancel);
    }

    [GeneratedRegex(@"\{[^}]+\}")]
    private static partial Regex Parameter();

    [GeneratedRegex(@"\b\d{6}\b")]
    private static partial Regex SixDigits();

    private sealed record AppRoute(string Key, HttpMethod Method, string Url);

    /// <summary>Sends its own cookies, and the page's anti-forgery cookie with a form, as a browser would.</summary>
    private sealed class Visitor(HttpClient client, string cookie)
    {
        public Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, HttpContent? content = null, string extra = "")
        {
            var request = new HttpRequestMessage(method, url) { Content = content };
            var cookies = string.Join("; ", new[] { cookie, extra }.Where(value => value.Length > 0));
            if (cookies.Length > 0)
            {
                request.Headers.Add("Cookie", cookies);
            }

            return client.SendAsync(request, Cancel);
        }

        public Task<HttpResponseMessage> GetAsync(string url)
        {
            return SendAsync(HttpMethod.Get, url);
        }

        /// <summary>The page's text, decoded (Razor encodes <c>+</c>, <c>=</c> and non-Latin letters).</summary>
        public async Task<string> PageAsync(string url)
        {
            var response = await GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Cancel));
        }

        /// <summary>Posts <paramref name="fields"/> to <paramref name="url"/> with the anti-forgery token of <paramref name="formPage"/>.</summary>
        public async Task<HttpResponseMessage> PostFormAsync(string formPage, string url, params (string Name, string Value)[] fields)
        {
            var form = await GetAsync(formPage);
            Assert.Equal(HttpStatusCode.OK, form.StatusCode);
            var token = Token().Match(await form.Content.ReadAsStringAsync(Cancel)).Groups[1].Value;
            var content = new FormUrlEncodedContent(
            [
                .. fields.Select(field => KeyValuePair.Create(field.Name, field.Value)),
                KeyValuePair.Create("__RequestVerificationToken", token)
            ]);

            return await SendAsync(HttpMethod.Post, url, content, Cookies(form));
        }

        public static string Cookies(HttpResponseMessage response)
        {
            return response.Headers.TryGetValues("Set-Cookie", out var cookies)
                ? string.Join("; ", cookies.Select(value => value.Split(';')[0]))
                : "";
        }
    }
}
