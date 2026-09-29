using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Application.Abstractions;
using Domain.Customers;
using Domain.Orders;
using Infrastructure.MultiTenancy;
using Infrastructure.Persistence;

namespace Integration.Tests;

/// <summary>
/// The pages that guide a newcomer: each role's home, the hub's day in steps, the shop's order form, the demo
/// sign-in and the guide. Each still keeps operators and shops apart.
/// </summary>
public partial class PortalPagesTests(WebAppFactory factory)
{
    private const string Password = "OneDrop#2026";

    [Fact]
    public async Task Hub_staff_land_on_hub_today_which_remembers_their_hub_and_nobody_else_opens_it()
    {
        WebAppFactory.RequireDatabase();
        var hub = await StaffSignInAsync("dhaka", "hub@dhaka.onedrop.test");
        var chattogramHub = await StaffSignInAsync("chattogram", "hub@chattogram.onedrop.test");
        var merchant = await StaffSignInAsync("dhaka", "gadget@dhaka.onedrop.test");

        var home = await hub.GetAsync("/", Cancel);
        var picker = await hub.GetStringAsync("/Hub", Cancel);
        var today = await hub.GetStringAsync("/Hub?hub=UTT", Cancel);
        var remembered = await hub.GetStringAsync("/Hub", Cancel);
        var shelves = await hub.GetStringAsync("/Hub/Shelves", Cancel);

        Assert.Equal("/Hub", home.Headers.Location!.OriginalString);
        Assert.Contains("/Hub?hub=UTT", picker);
        Assert.Contains("Uttara hub today", today);
        Assert.Contains("Pick up from the shops", today);
        Assert.Contains("/Hub/Cash?hub=UTT", today);
        Assert.Contains("Uttara hub today", remembered);
        // The step bar carries the remembered hub, while the page itself still asks until one is named
        Assert.Contains("/Hub/Scan?hub=UTT", shelves);
        Assert.Equal(HttpStatusCode.NotFound, (await chattogramHub.GetAsync("/Hub?hub=UTT", Cancel)).StatusCode);
        Assert.StartsWith("/Account/AccessDenied", (await merchant.GetAsync("/Hub", Cancel)).Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task A_shop_types_in_an_order_which_is_priced_and_grouped_as_by_the_api_and_saved_once()
    {
        WebAppFactory.RequireDatabase();
        var gadget = await StaffSignInAsync("dhaka", "gadget@dhaka.onedrop.test");
        var fashion = await StaffSignInAsync("dhaka", "fashion@dhaka.onedrop.test");
        var phone = NewPhone();
        var area = await AreaIdAsync("Mirpur 10");

        var form = await gadget.GetStringAsync("/Merchant/NewOrder", Cancel);
        var key = FormKey().Match(form).Groups[1].Value;
        var fields = new Dictionary<string, string>
        {
            ["Form.Key"] = key,
            ["Form.Name"] = "Form Customer",
            ["Form.Phone"] = phone,
            ["Form.AreaId"] = area.ToString(),
            ["Form.Line1"] = "House 7, Road 2",
            ["Form.Description"] = "Headphones",
            ["Form.Parcels"] = "2",
            ["Form.WeightKg"] = "0.5",
            ["Form.Cod"] = "1200"
        };
        var posted = await PostAsync(gadget, "/Merchant/NewOrder", fields);
        var again = await PostAsync(gadget, "/Merchant/NewOrder", fields);
        var created = await gadget.GetStringAsync("/Merchant/NewOrder", Cancel);
        var second = await PostAsync(fashion, "/Merchant/NewOrder", new(fields)
        {
            ["Form.Key"] = FormKey().Match(await fashion.GetStringAsync("/Merchant/NewOrder", Cancel)).Groups[1].Value
        });
        var invalid = await PostAsync(gadget, "/Merchant/NewOrder", new(fields) { ["Form.Phone"] = "", ["Form.Key"] = Guid.NewGuid().ToString("N") });

        Assert.Equal(HttpStatusCode.Redirect, posted.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, again.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, second.StatusCode);
        var orders = await OrdersOfAsync(phone);
        Assert.Equal(2, orders.Length);
        var mine = orders.Single(order => order.Merchant == "Gadget BD");
        var theirs = orders.Single(order => order.Merchant == "Fashion House");
        Assert.Contains(mine.Number, created);
        Assert.Contains("Order created", created);
        Assert.Equal((2, 1200m, DeliverySpeed.Combine, CustomerStep.Confirm), (mine.Packages, mine.Cod, mine.Speed, mine.Step));
        Assert.Equal(mine.Group, theirs.Group);
        var tenant = await TenantAsync("dhaka");
        Assert.Equal((tenant.BaseDeliveryFee, tenant.ExtraShopFee), (mine.Fee, theirs.Fee));
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        Assert.Contains("The order was not saved", await invalid.Content.ReadAsStringAsync(Cancel));
        Assert.Equal(2, (await OrdersOfAsync(phone)).Length);
        Assert.DoesNotContain(theirs.Number, await gadget.GetStringAsync("/Merchant/Orders", Cancel));
    }

    [Fact]
    public async Task The_demo_sign_in_offers_only_this_operators_logins_and_the_guide_is_open_to_all()
    {
        WebAppFactory.RequireDatabase();
        var anonymous = Client("dhaka");

        var login = await anonymous.GetStringAsync("/Account/Login", Cancel);
        var guide = await anonymous.GetAsync("/Guide", Cancel);
        var landing = await anonymous.GetStringAsync("/", Cancel);

        Assert.Contains("hub@dhaka.onedrop.test", login);
        Assert.Contains("rider@dhaka.onedrop.test", login);
        Assert.DoesNotContain("chattogram.onedrop.test", login);
        Assert.DoesNotContain("admin@onedrop.test", login);
        Assert.Equal(HttpStatusCode.OK, guide.StatusCode);
        Assert.Contains("hub@dhaka.onedrop.test", await guide.Content.ReadAsStringAsync(Cancel));
        Assert.Contains("Who are you?", landing);
    }

    private async Task<HttpResponseMessage> PostAsync(HttpClient client, string url, Dictionary<string, string> fields)
    {
        var token = Token().Match(await client.GetStringAsync(url, Cancel)).Groups[1].Value;

        return await client.PostAsync(url, new FormUrlEncodedContent(new Dictionary<string, string>(fields)
        {
            ["__RequestVerificationToken"] = token
        }), Cancel);
    }

    private HttpClient Client(string slug)
    {
        return factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri($"http://{slug}.localhost"),
            AllowAutoRedirect = false
        });
    }

    /// <summary>Signs a demo staff user in on their operator's subdomain. Keeps the sign-in cookie.</summary>
    private async Task<HttpClient> StaffSignInAsync(string slug, string email)
    {
        var client = Client(slug);
        var signedIn = await PostAsync(client, "/Account/Login", new()
        {
            ["Input.Email"] = email,
            ["Input.Password"] = Password
        });
        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);

        return client;
    }

    private async Task<TenantInfo> TenantAsync(string slug)
    {
        return (await factory.Services.GetRequiredService<ITenantCatalog>().FindBySlugAsync(slug, Cancel))!;
    }

    private async Task<long> AreaIdAsync(string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(await TenantAsync("dhaka"));
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.Areas.Where(a => a.Name == name).Select(a => a.Id).SingleAsync(Cancel);
    }

    private async Task<FormOrder[]> OrdersOfAsync(string phone)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(await TenantAsync("dhaka"));
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var e164 = PhoneNumber.Parse(phone).Value.Value;

        return await (
            from order in db.Orders
            join customer in db.Customers on order.CustomerId equals customer.Id
            join merchant in db.Merchants on order.MerchantId equals merchant.Id
            where customer.Phone == e164
            select new FormOrder(
                order.Number,
                merchant.Name,
                order.DeliveryGroupId,
                order.Packages.Count,
                order.CodAmount,
                order.Speed,
                order.CustomerStep,
                order.AddedFee))
            .ToArrayAsync(Cancel);
    }

    private static string NewPhone()
    {
        return "017" + Random.Shared.Next(0, 100_000_000).ToString("D8");
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [GeneratedRegex("""name="__RequestVerificationToken" type="hidden" value="([^"]+)""")]
    private static partial Regex Token();

    [GeneratedRegex("""name="Form.Key" value="([0-9a-f]{32})""")]
    private static partial Regex FormKey();

    private sealed record FormOrder(
        string Number,
        string Merchant,
        long Group,
        int Packages,
        decimal Cod,
        DeliverySpeed Speed,
        CustomerStep Step,
        decimal Fee);
}
