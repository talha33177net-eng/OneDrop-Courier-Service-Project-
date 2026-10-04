using System.Net;
using Microsoft.EntityFrameworkCore;
using Domain.Delivery;
using Domain.Parcels;

namespace Integration.Tests;

/// <summary>Each role's home, public tracking, the admin's riders and rate card, and a pickup from request to collection.</summary>
public class PortalPagesTests(WebAppFactory factory) : AppTests(factory)
{
    [Theory]
    [InlineData("admin@onedrop.test", "/Admin")]
    [InlineData("hub@onedrop.test", "/Hub")]
    [InlineData("fashion@onedrop.test", "/Merchant")]
    [InlineData("rider@onedrop.test", "/Rider")]
    public async Task Each_role_lands_on_its_own_home(string email, string home)
    {
        WebAppFactory.RequireDatabase();
        var visitor = await SignInAsync("onedrop", email);

        var landing = await visitor.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, landing.StatusCode);
        Assert.Equal(home, landing.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync(home)).StatusCode);
    }

    [Fact]
    public async Task Anyone_tracks_a_parcel_by_its_code_at_its_own_courier_only()
    {
        WebAppFactory.RequireDatabase();
        var shop = await NewMerchantAsync();
        var code = await BookAsync(shop.ApiKey, area: "Banani");

        var page = await Visit("onedrop").PageAsync($"/Track?code={code}");
        Assert.Contains(code, page);
        Assert.Contains("Pending pickup", page);
        Assert.DoesNotContain("Rahim Uddin", page);
        Assert.DoesNotContain("House 22", page);

        Assert.Contains("could not find a parcel", await Visit("rival").PageAsync($"/Track?code={code}"));
        Assert.Contains("could not find a parcel", await Visit("onedrop").PageAsync("/Track?code=OD99999999"));
        Assert.Equal(HttpStatusCode.NotFound, (await Visit("").GetAsync($"/Track?code={code}")).StatusCode);
        Assert.Contains("Sylhet Sadar", await Visit("onedrop").PageAsync("/Coverage"));
    }

    [Fact]
    public async Task The_admin_adds_a_rider_who_signs_in_to_the_rider_app_and_another_couriers_hub_is_refused()
    {
        WebAppFactory.RequireDatabase();
        var admin = await SignInAsync("onedrop", "admin@onedrop.test");
        var email = $"{Guid.NewGuid():N}@rider.test";
        var name = $"Rider {Guid.NewGuid():N}"[..14];
        var gul = await QueryAsync("onedrop", db => db.Hubs.Where(h => h.Code == "GUL").Select(h => h.Id).SingleAsync(Cancel));
        var rivalHub = await QueryAsync("rival", db => db.Hubs.Select(h => h.Id).FirstAsync(Cancel));

        var refused = await admin.SubmitAsync("/Admin/Riders", "/Admin/Riders?handler=Add", ("name", name), ("phone", NewPhone()), ("hubId", $"{rivalHub}"), ("email", email), ("password", WebAppFactory.Password));
        Assert.DoesNotContain($"{name} is added", refused);
        Assert.False(await QueryAsync("onedrop", db => db.Riders.AnyAsync(r => r.Name == name, Cancel)));

        Assert.Contains($"{name} is added", await admin.SubmitAsync("/Admin/Riders", "/Admin/Riders?handler=Add", ("name", name), ("phone", NewPhone()), ("hubId", $"{gul}"), ("email", email), ("password", WebAppFactory.Password)));
        var rider = await SignInAsync("onedrop", email);
        Assert.Equal(HttpStatusCode.OK, (await rider.GetAsync("/Rider")).StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, (await rider.GetAsync("/Admin")).StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, (await rider.GetAsync("/Hub")).StatusCode);
    }

    [Fact]
    public async Task Only_the_admin_sees_and_changes_the_rate_card_and_merchants_see_it_read_only()
    {
        WebAppFactory.RequireDatabase();
        var admin = await SignInAsync("onedrop", "admin@onedrop.test");
        var hub = await SignInAsync("onedrop", "hub@onedrop.test");
        var shop = await NewMerchantAsync();

        Assert.Contains("Outside city", await admin.PageAsync("/Admin/Rates"));
        Assert.NotEqual(HttpStatusCode.OK, (await hub.GetAsync("/Admin/Rates")).StatusCode);
        await hub.PostFormAsync("/Hub", "/Admin/Rates", ("area", "InsideCity"), ("includedKg", "1"), ("baseCharge", "1"), ("extraKgCharge", "1"), ("codChargePercent", "0"), ("returnCharge", "0"));
        Assert.Contains("৳120", await (await SignInAsync("onedrop", shop.Email)).PageAsync("/Merchant/Pricing"));
        Assert.Equal(60, await QueryAsync("onedrop", db => db.DeliveryRates.Where(r => r.ServiceArea == Domain.Pricing.ServiceArea.InsideCity).Select(r => r.BaseCharge).SingleAsync(Cancel)));
    }

    [Fact]
    public async Task A_merchant_asks_for_a_pickup_the_hub_sends_a_rider_and_the_rider_collects_the_parcels()
    {
        WebAppFactory.RequireDatabase();
        var shop = await NewMerchantAsync(area: "Mirpur 10");
        var rider = await NewRiderAsync("MIR");
        var codes = new[] { await BookAsync(shop.ApiKey), await BookAsync(shop.ApiKey) };
        var merchant = await SignInAsync("onedrop", shop.Email);
        var point = await QueryAsync("onedrop", db => db.PickupPoints.Where(p => p.MerchantId == shop.Id).Select(p => p.Id).SingleAsync(Cancel));
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Dhaka")));

        Assert.Contains("Pickup requested", await merchant.SubmitAsync("/Merchant/Pickups", "/Merchant/Pickups", ("PointId", $"{point}"), ("Date", today.ToString("yyyy-MM-dd")), ("Expected", "2"), ("Note", "Gate 2")));
        var pickup = await QueryAsync("onedrop", db => db.PickupRequests.SingleAsync(p => p.MerchantId == shop.Id, Cancel));

        var hub = await SignInAsync("onedrop", "hub@onedrop.test");
        Assert.Contains(shop.Name, await hub.PageAsync("/Hub/Pickups?hub=MIR"));
        Assert.Equal(HttpStatusCode.NotFound, (await hub.PostFormAsync("/Hub/Pickups?hub=MIR", "/Hub/Pickups?hub=GUL&handler=Assign", ("id", $"{pickup.Id}"), ("riderId", $"{rider.Id}"))).StatusCode);
        Assert.Contains("Rider assigned", await hub.SubmitAsync("/Hub/Pickups?hub=MIR", "/Hub/Pickups?hub=MIR&handler=Assign", ("id", $"{pickup.Id}"), ("riderId", $"{rider.Id}")));

        var app = await SignInAsync("onedrop", rider.Email);
        Assert.Contains(shop.Name, await app.PageAsync("/Rider/Pickups"));
        Assert.Contains("2 parcels collected", await app.SubmitAsync("/Rider/Pickups", "/Rider/Pickups", ("id", $"{pickup.Id}"), ("codes", codes[0]), ("codes", codes[1])));

        foreach (var code in codes)
        {
            Assert.Equal(ParcelStatus.PickedUp, (await ParcelAsync(code)).Status);
        }

        Assert.Equal(PickupStatus.Completed, await QueryAsync("onedrop", db => db.PickupRequests.Where(p => p.Id == pickup.Id).Select(p => p.Status).SingleAsync(Cancel)));

        // Another merchant cannot cancel it
        var other = await SignInAsync("onedrop", (await NewMerchantAsync()).Email);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostFormAsync("/Merchant/Pickups", "/Merchant/Pickups?handler=Cancel", ("id", $"{pickup.Id}"))).StatusCode);
    }
}
