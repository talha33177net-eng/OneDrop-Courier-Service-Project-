using System.Net;
using Microsoft.EntityFrameworkCore;

namespace Integration.Tests;

/// <summary>The paper a rider carries: the parcels of their run with the door, the phone and the cash to collect.</summary>
public class RunSheetTests(WebAppFactory factory) : AppTests(factory)
{
    [Fact]
    public async Task A_run_sheet_lists_the_parcels_handed_over_and_drops_the_ones_already_recorded()
    {
        WebAppFactory.RequireDatabase();
        var shop = await NewMerchantAsync(area: "Banani");
        var rider = await NewRiderAsync("GUL");
        var first = await BookAsync(shop.ApiKey, area: "Gulshan 2", cod: 1250);
        var second = await BookAsync(shop.ApiKey, area: "Banani", cod: 500);
        var hub = await SignInAsync("onedrop", "hub@onedrop.test");
        foreach (var code in new[] { first, second })
        {
            await hub.SubmitAsync("/Hub/Scan?hub=GUL", "/Hub/Scan?mode=Receive&hub=GUL", ("Code", code));
        }

        var handed = await hub.SubmitAsync(
            "/Hub/Assign?hub=GUL",
            "/Hub/Assign?hub=GUL",
            ("riderId", $"{rider.Id}"),
            ("codes", first),
            ("codes", second));
        Assert.Contains("2 parcels handed over", handed);
        Assert.Contains("Print the run sheet", handed);

        var runId = await QueryAsync("onedrop", db => db.DeliveryRuns.Where(r => r.RiderId == rider.Id).Select(r => r.Id).SingleAsync(Cancel));
        var sheet = await hub.PageAsync($"/Hub/RunSheet/{runId}?hub=GUL");
        Assert.Contains(first, sheet);
        Assert.Contains(second, sheet);
        Assert.Contains("Rahim Uddin", sheet);
        Assert.Contains("House 22, Road 4", sheet);
        Assert.Contains("৳1,750", sheet);

        // Once the rider records a parcel it leaves the sheet: what is left is the work still in their bag
        var door = await SignInAsync("onedrop", rider.Email);
        await door.SubmitAsync($"/Rider/Delivery/{first}", $"/Rider/Delivery/{first}?handler=Deliver", ("collected", "1250"), ("reason", ""));
        var later = await hub.PageAsync($"/Hub/RunSheet/{runId}?hub=GUL");
        Assert.DoesNotContain(first, later);
        Assert.Contains(second, later);
        Assert.Contains("৳500", later);
    }

    [Fact]
    public async Task A_run_sheet_of_another_hub_or_another_courier_is_not_found()
    {
        WebAppFactory.RequireDatabase();
        var shop = await NewMerchantAsync(area: "Uttara Sector 7");
        var rider = await NewRiderAsync("UTT");
        var code = await BookAsync(shop.ApiKey, area: "Uttara Sector 7", cod: 300);
        var hub = await SignInAsync("onedrop", "hub@onedrop.test");
        await hub.SubmitAsync("/Hub/Scan?hub=UTT", "/Hub/Scan?mode=Receive&hub=UTT", ("Code", code));
        await hub.SubmitAsync("/Hub/Assign?hub=UTT", "/Hub/Assign?hub=UTT", ("riderId", $"{rider.Id}"), ("codes", code));
        var runId = await QueryAsync("onedrop", db => db.DeliveryRuns.Where(r => r.RiderId == rider.Id).Select(r => r.Id).SingleAsync(Cancel));

        Assert.Equal(HttpStatusCode.NotFound, (await hub.GetAsync($"/Hub/RunSheet/{runId}?hub=MIR")).StatusCode);
        var rival = await SignInAsync("rival", "hub@rival.test");
        Assert.Equal(HttpStatusCode.NotFound, (await rival.GetAsync($"/Hub/RunSheet/{runId}?hub=UTT")).StatusCode);
    }
}
