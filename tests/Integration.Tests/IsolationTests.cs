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
using Domain.Parcels;
using Domain.Payments;
using Infrastructure.Persistence;

namespace Integration.Tests;

/// <summary>
/// The isolation sweep. Every page and endpoint the app maps is listed here with what another courier, or another
/// merchant or rider of the same courier, gets when it asks for a record that is not its own; a route added without a
/// line here fails the sweep. A sign-in is refused on every route of another courier's host and of the platform's.
/// </summary>
public partial class IsolationTests(WebAppFactory factory) : AppTests(factory)
{
    [Fact]
    public async Task Another_courier_merchant_or_rider_reaches_nothing_of_a_parcel_on_any_page_or_endpoint()
    {
        WebAppFactory.RequireDatabase();
        var shop = await NewMerchantAsync();
        var otherShop = await NewMerchantAsync();
        var rider = await NewRiderAsync("MIR");
        var otherRider = await NewRiderAsync("MIR");
        var phone = NewPhone();
        var waiting = await BookAsync(shop.ApiKey, area: "Pallabi", phone: phone);
        var outCode = await BookAsync(shop.ApiKey, area: "Pallabi", phone: phone);
        var payout = await PaidAsync(shop);
        await QueryAsync("onedrop", async db =>
        {
            var parcel = await db.Parcels.SingleAsync(p => p.TrackingCode == outCode, Cancel);
            parcel.ReceiveAt(parcel.DeliveryHubId);

            return await db.SaveChangesAsync(Cancel);
        });
        var (secret, keyPrefix, account) = await QueryAsync("onedrop", async db =>
        {
            var merchant = await db.Merchants.SingleAsync(m => m.Id == shop.Id, Cancel);
            merchant.SetWebhook($"https://{Guid.NewGuid():N}.example/hook");
            await db.SaveChangesAsync(Cancel);
            var prefix = await db.MerchantApiKeys.Where(k => k.MerchantId == shop.Id).Select(k => k.Prefix).FirstAsync(Cancel);

            return (merchant.WebhookSecret!, prefix, merchant.PayoutAccount!);
        });

        var owner = await SignInAsync("onedrop", shop.Email);
        var other = await SignInAsync("onedrop", otherShop.Email);
        var rivalShop = await SignInAsync("rival", "shop@rival.test");
        var admin = await SignInAsync("onedrop", "admin@onedrop.test");
        var hub = await SignInAsync("onedrop", "hub@onedrop.test");
        Assert.Contains("1 parcel handed over", await hub.SubmitAsync("/Hub/Assign?hub=MIR", "/Hub/Assign?hub=MIR", ("riderId", $"{rider.Id}"), ("codes", outCode)));
        var rivalAdmin = await SignInAsync("rival", "admin@rival.test");
        var rivalHub = await SignInAsync("rival", "hub@rival.test");
        var ownRider = await SignInAsync("onedrop", rider.Email);
        var neighbour = await SignInAsync("onedrop", otherRider.Email);
        var rivalRider = await SignInAsync("rival", "rider@rival.test");
        var ours = Factory.ClientFor(shop.ApiKey);
        var theirs = Factory.ClientFor(otherShop.ApiKey);
        var rival = Factory.ClientFor(WebAppFactory.Rival);
        var publicRival = Visit("rival");

        (string Route, Func<Task> Check)[] sweep =
        [
            // Public pages: each courier's own site, and the platform's says nothing of any parcel
            ("/Index", async () =>
            {
                Assert.Contains("OneDrop Courier", await Visit("onedrop").PageAsync("/"));
                Assert.DoesNotContain("OneDrop Courier", await publicRival.PageAsync("/"));
            }),
            ("/Track", async () =>
            {
                Assert.Contains(outCode, await Visit("onedrop").PageAsync($"/Track?code={outCode}"));
                Assert.Contains("could not find a parcel", await publicRival.PageAsync($"/Track?code={outCode}"));
                Assert.DoesNotContain(phone[3..], await Visit("onedrop").PageAsync($"/Track?code={outCode}"));
            }),
            ("/Coverage", async () => Assert.DoesNotContain("Gulshan", await publicRival.PageAsync("/Coverage"))),
            ("/Error", Fixed("the error page shows no record")),
            ("/Account/AccessDenied", Fixed("a fixed message")),
            ("/Account/Logout", Fixed("signs the visitor out, nothing else")),
            ("/Account/Login", async () =>
            {
                var refused = await publicRival.PostFormAsync("/Account/Login", "/Account/Login", ("Input.Email", shop.Email), ("Input.Password", WebAppFactory.Password));
                Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
            }),
            ("/Account/Register", async () => Assert.DoesNotContain("Gulshan", await publicRival.PageAsync("/Account/Register"))),

            // Merchant panel: only the parcel's merchant
            ("/Merchant/Index", async () => await NotSeenByOthersAsync("/Merchant", shop.Name)),
            ("/Merchant/Parcels", async () => await OnlyTheShopSeesAsync("/Merchant/Parcels", outCode)),
            ("/Merchant/Parcel", async () => await NotFoundForOtherShopsAsync($"/Merchant/Parcel/{outCode}")),
            ("/Merchant/EditParcel", async () => await NotFoundForOtherShopsAsync($"/Merchant/EditParcel/{waiting}")),
            ("/Merchant/NewParcel", async () => Assert.DoesNotContain("Gulshan", await rivalShop.PageAsync("/Merchant/NewParcel"))),
            ("/Merchant/BulkUpload", async () =>
            {
                Assert.Equal(HttpStatusCode.OK, (await other.GetAsync("/Merchant/BulkUpload")).StatusCode);
                Assert.DoesNotContain(waiting, await other.PageAsync("/Merchant/BulkUpload"));
            }),
            ("/Merchant/Labels", async () => await OnlyTheShopSeesAsync("/Merchant/Labels", waiting)),
            ("/Merchant/Pickups", async () => await NotSeenByOthersAsync("/Merchant/Pickups", shop.Name)),
            ("/Merchant/Payments", async () => await OnlyTheShopSeesAsync("/Merchant/Payments", payout)),
            ("/Merchant/Payment", async () => await NotFoundForOtherShopsAsync($"/Merchant/Payment/{payout}")),
            ("/Merchant/FraudCheck", async () =>
            {
                Assert.DoesNotContain(shop.Name, await other.PageAsync($"/Merchant/FraudCheck?phone={phone}"));
                Assert.Contains("New customer", await rivalShop.PageAsync($"/Merchant/FraudCheck?phone={phone}"));
            }),
            ("/Merchant/Pricing", async () => Assert.DoesNotContain("৳120", await rivalShop.PageAsync("/Merchant/Pricing"))),
            ("/Merchant/Settings", async () => await OnlyTheShopSeesAsync("/Merchant/Settings", Domain.Common.PhoneNumber.Parse(account).Value.Local)),
            ("/Merchant/ApiKeys", async () => await OnlyTheShopSeesAsync("/Merchant/ApiKeys", keyPrefix)),
            ("/Merchant/Webhook", async () => await OnlyTheShopSeesAsync("/Merchant/Webhook", secret)),

            // Hub pages: the courier's own staff; another courier's staff and admin get a 404 for this courier's hub
            ("/Hub/Index", async () => await HubOnlyAsync("/Hub?hub=MIR")),
            ("/Hub/Scan", async () =>
            {
                await HubOnlyAsync("/Hub/Scan?hub=MIR");
                var scanned = await rivalHub.SubmitAsync("/Hub/Scan", "/Hub/Scan", ("Code", waiting), ("Mode", "Receive"));
                Assert.DoesNotContain("Received", scanned);
            }),
            ("/Hub/Pickups", async () => await HubOnlyAsync("/Hub/Pickups?hub=MIR")),
            ("/Hub/Assign", async () => await HubOnlyAsync("/Hub/Assign?hub=MIR")),
            ("/Hub/Runs", async () => await HubOnlyAsync("/Hub/Runs?hub=MIR")),
            ("/Hub/Parcels", async () =>
            {
                Assert.Contains(outCode, await hub.PageAsync($"/Hub/Parcels?search={outCode}"));
                Assert.DoesNotContain(shop.Name, await rivalHub.PageAsync($"/Hub/Parcels?search={outCode}"));
            }),
            ("/Hub/Parcel", async () =>
            {
                Assert.Equal(HttpStatusCode.OK, (await hub.GetAsync($"/Hub/Parcel/{outCode}")).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await rivalHub.GetAsync($"/Hub/Parcel/{outCode}")).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await rivalAdmin.GetAsync($"/Hub/Parcel/{outCode}")).StatusCode);
            }),
            ("/Hub/Labels", async () =>
            {
                Assert.Contains(waiting, await hub.PageAsync($"/Hub/Labels?codes={waiting}"));
                Assert.DoesNotContain(waiting, await rivalHub.PageAsync($"/Hub/Labels?codes={waiting}"));
            }),

            // Rider app: only the rider the parcel was given to
            ("/Rider/Index", async () =>
            {
                Assert.Contains(outCode, await ownRider.PageAsync("/Rider"));
                Assert.DoesNotContain(outCode, await neighbour.PageAsync("/Rider"));
                Assert.DoesNotContain(outCode, await rivalRider.PageAsync("/Rider"));
            }),
            ("/Rider/Delivery", async () =>
            {
                Assert.Equal(HttpStatusCode.OK, (await ownRider.GetAsync($"/Rider/Delivery/{outCode}")).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await neighbour.GetAsync($"/Rider/Delivery/{outCode}")).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await rivalRider.GetAsync($"/Rider/Delivery/{outCode}")).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await neighbour.PostFormAsync("/Rider", $"/Rider/Delivery/{outCode}?handler=Refuse", ("reason", "Test"))).StatusCode);
            }),
            ("/Rider/Pickups", async () => Assert.DoesNotContain(shop.Name, await neighbour.PageAsync("/Rider/Pickups"))),

            // Courier admin: only the courier's own admin
            ("/Admin/Index", async () => Assert.DoesNotContain("Mirpur hub", await rivalAdmin.PageAsync("/Admin"))),
            ("/Admin/Merchants", async () =>
            {
                Assert.Contains(shop.Name, await admin.PageAsync($"/Admin/Merchants?search={Uri.EscapeDataString(shop.Name)}"));
                Assert.DoesNotContain(shop.Name, await rivalAdmin.PageAsync("/Admin/Merchants"));
            }),
            ("/Admin/Merchant", async () =>
            {
                Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/Admin/Merchant/{shop.Id}")).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await rivalAdmin.GetAsync($"/Admin/Merchant/{shop.Id}")).StatusCode);
            }),
            ("/Admin/NewMerchant", async () => Assert.DoesNotContain("Gulshan", await rivalAdmin.PageAsync("/Admin/NewMerchant"))),
            ("/Admin/Riders", async () => Assert.DoesNotContain("Rafiq Hasan", await rivalAdmin.PageAsync("/Admin/Riders"))),
            ("/Admin/Payouts", async () =>
            {
                Assert.Contains(payout, await admin.PageAsync("/Admin/Payouts"));
                Assert.DoesNotContain(payout, await rivalAdmin.PageAsync("/Admin/Payouts"));
            }),
            ("/Admin/Payout", async () =>
            {
                Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/Admin/Payout/{payout}")).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await rivalAdmin.GetAsync($"/Admin/Payout/{payout}")).StatusCode);
            }),
            ("/Admin/Rates", async () => Assert.DoesNotContain("৳120", await rivalAdmin.PageAsync("/Admin/Rates"))),
            ("/Admin/Coverage", async () => Assert.DoesNotContain("Gulshan", await rivalAdmin.PageAsync("/Admin/Coverage"))),
            ("/Admin/Messages", async () => Assert.DoesNotContain(outCode, await rivalAdmin.PageAsync("/Admin/Messages"))),

            // The platform's list of couriers and the jobs dashboard are for platform admins only
            ("/Platform/Tenants", async () => Assert.NotEqual(HttpStatusCode.OK, (await Visit("", "").GetAsync("/Platform/Tenants")).StatusCode)),
            ("/jobs/{**path}", async () => Assert.NotEqual(HttpStatusCode.OK, (await admin.GetAsync("/jobs")).StatusCode)),

            // Development's fake gateways show every courier by design; they are not there in Production (below)
            ("/Dev/Sms", Fixed("Development only")),
            ("/Dev/Payouts", Fixed("Development only")),
            ("/Dev/Webhooks", Fixed("Development only")),

            // The merchant API: a merchant's key reaches its own parcels only, another courier's key none
            ("POST /api/v1/parcels", async () =>
            {
                var booked = await rival.PostAsJsonAsync("/api/v1/parcels", new { RecipientName = "X", RecipientPhone = phone, RecipientAddress = "X", Area = "Gulshan 2", CodAmount = 0, WeightKg = 0.5 }, Cancel);
                Assert.Contains("parcel.area.unknown", await booked.Content.ReadAsStringAsync(Cancel));
            }),
            ("GET /api/v1/parcels/{code}", async () =>
            {
                Assert.Equal(HttpStatusCode.OK, (await ours.GetAsync($"/api/v1/parcels/{outCode}", Cancel)).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await theirs.GetAsync($"/api/v1/parcels/{outCode}", Cancel)).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await rival.GetAsync($"/api/v1/parcels/{outCode}", Cancel)).StatusCode);
            }),
            ("POST /api/v1/parcels/{code}/cancel", async () =>
            {
                Assert.Equal(HttpStatusCode.NotFound, (await theirs.PostAsJsonAsync($"/api/v1/parcels/{waiting}/cancel", new { }, Cancel)).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await rival.PostAsJsonAsync($"/api/v1/parcels/{waiting}/cancel", new { }, Cancel)).StatusCode);
            }),
            ("GET /api/v1/charge", async () => Assert.Contains("area.unknown", await (await rival.GetAsync("/api/v1/charge?area=Gulshan%202&weightKg=1&codAmount=0", Cancel)).Content.ReadAsStringAsync(Cancel))),
            ("GET /api/v1/areas", async () => Assert.DoesNotContain("Gulshan", await rival.GetStringAsync("/api/v1/areas", Cancel))),

            // Live dashboards hear "changed" only, and only staff connect
            ("/hubs/operations", async () =>
            {
                Assert.Equal(HttpStatusCode.Forbidden, (await owner.SendAsync(HttpMethod.Post, "/hubs/operations/negotiate?negotiateVersion=1")).StatusCode);
                Assert.Equal(HttpStatusCode.Unauthorized, (await Visit("onedrop").SendAsync(HttpMethod.Post, "/hubs/operations/negotiate?negotiateVersion=1")).StatusCode);
            })
        ];

        var mapped = Routes().Select(route => route.Key).ToArray();
        var swept = sweep.Select(line => line.Route).ToArray();
        Assert.True(
            mapped.Order().SequenceEqual(swept.Order()),
            $"Add each new route to the sweep with what another courier, merchant or rider gets there: {string.Join(", ", mapped.Except(swept))}. " +
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

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));

        // Nothing changed: the waiting parcel is still waiting, the parcel out is still with its rider
        var stillWaiting = await ParcelAsync(waiting);
        var stillOut = await ParcelAsync(outCode);
        Assert.Equal((ParcelStatus.Pending, null), (stillWaiting.Status, stillWaiting.CurrentHubId));
        Assert.Equal((ParcelStatus.OutForDelivery, rider.Id), (stillOut.Status, stillOut.RiderId));

        // A page that holds no record of anyone's; the reason is the sweep's documentation
        static Func<Task> Fixed(string because)
        {
            return () => Task.CompletedTask;
        }

        async Task NotSeenByOthersAsync(string url, string text)
        {
            Assert.DoesNotContain(text, await other.PageAsync(url));
            Assert.DoesNotContain(text, await rivalShop.PageAsync(url));
        }

        async Task OnlyTheShopSeesAsync(string url, string text)
        {
            Assert.Contains(text, await owner.PageAsync(url));
            await NotSeenByOthersAsync(url, text);
        }

        async Task NotFoundForOtherShopsAsync(string url)
        {
            Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await rivalShop.GetAsync(url)).StatusCode);
        }

        async Task HubOnlyAsync(string url)
        {
            Assert.Equal(HttpStatusCode.OK, (await hub.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await rivalHub.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await rivalAdmin.GetAsync(url)).StatusCode);
        }
    }

    [Fact]
    public async Task A_sign_in_is_refused_on_every_route_of_another_courier_and_of_the_platform()
    {
        WebAppFactory.RequireDatabase();
        (string Who, string Home, string Cookie)[] signIns =
        [
            ("merchant", "onedrop", await SignInCookieAsync("onedrop", "gadget@onedrop.test")),
            ("hub staff", "onedrop", await SignInCookieAsync("onedrop", "hub@onedrop.test")),
            ("courier admin", "onedrop", await SignInCookieAsync("onedrop", "admin@onedrop.test")),
            ("rider", "onedrop", await SignInCookieAsync("onedrop", "rider@onedrop.test")),
            ("platform admin", "", await SignInCookieAsync("", "admin@platform.test"))
        ];
        var routes = Routes();

        var let = new List<string>();
        foreach (var (who, home, cookie) in signIns)
        {
            Assert.NotEqual(HttpStatusCode.Forbidden, (await Visit(home, cookie).GetAsync("/")).StatusCode);
            foreach (var host in new[] { "onedrop", "rival", "" }.Where(host => host != home))
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
    public async Task The_pages_that_show_every_courier_are_not_there_outside_development()
    {
        WebAppFactory.RequireDatabase();
        await using var production = Factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        var client = production.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://onedrop.localhost"),
            AllowAutoRedirect = false
        });

        foreach (var url in new[] { "/Dev/Sms", "/Dev/Payouts", "/Dev/Webhooks" })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(url, Cancel)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/Dev/Webhooks", new StringContent("{}"), Cancel)).StatusCode);
        Assert.DoesNotContain("onedrop.test", await client.GetStringAsync("/Account/Login", Cancel));
    }

    /// <summary>A delivered parcel of the merchant's, paid out to it; returns the payout's number.</summary>
    private async Task<string> PaidAsync(TestMerchant shop)
    {
        var code = await BookAsync(shop.ApiKey, area: "Pallabi", cod: 500);

        return await QueryAsync("onedrop", async db =>
        {
            var parcel = await db.Parcels.SingleAsync(p => p.TrackingCode == code, Cancel);
            var rider = await db.Riders.Where(r => r.HubId == parcel.DeliveryHubId).Select(r => r.Id).FirstAsync(Cancel);
            parcel.ReceiveAt(parcel.DeliveryHubId);
            parcel.AssignTo(rider, parcel.DeliveryHubId);
            Assert.True(parcel.Deliver(500, null, DateTime.UtcNow).IsSuccess);
            var day = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
            var lines = LedgerEntry.For(parcel, day).ToList();
            db.LedgerEntries.AddRange(lines);
            await db.SaveChangesAsync(Cancel);
            var merchant = await db.Merchants.SingleAsync(m => m.Id == shop.Id, Cancel);
            var payout = Payout.Of(merchant, day, lines)!;
            db.Payouts.Add(payout);
            await db.SaveChangesAsync(Cancel);

            return (await db.Payouts.AsNoTracking().SingleAsync(p => p.Id == payout.Id, Cancel)).Number;
        });
    }

    /// <summary>
    /// Every page, API action, SignalR hub and dashboard the app maps, once each, with a URL that reaches it (any id
    /// is 1). Static files are left out: they hold no records.
    /// </summary>
    private IReadOnlyList<AppRoute> Routes()
    {
        return
        [
            .. Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
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

    [GeneratedRegex(@"\{[^}]+\}")]
    private static partial Regex Parameter();

    private sealed record AppRoute(string Key, HttpMethod Method, string Url);
}
