using Microsoft.EntityFrameworkCore;
using Domain.Delivery;
using Domain.Parcels;

namespace Integration.Tests;

/// <summary>Returns a hub sends back to their merchant with a rider, and the merchant signing for them.</summary>
public class ReturnListsTests(WebAppFactory factory) : AppTests(factory)
{
    [Fact]
    public async Task A_hub_sends_returns_with_a_rider_who_hands_them_over_and_the_merchant_confirms_them()
    {
        WebAppFactory.RequireDatabase();
        var shop = await NewMerchantAsync(area: "Banani");
        var rider = await NewRiderAsync("GUL");
        var first = await ReturningAsync(shop, "Gulshan 2");
        var second = await ReturningAsync(shop, "Gulshan 2");
        var hub = await SignInAsync("onedrop", "hub@onedrop.test");

        var waiting = await hub.PageAsync("/Hub/Returns?hub=GUL");
        Assert.Contains(first, waiting);
        Assert.Contains(shop.Name, waiting);
        var sent = await hub.SubmitAsync("/Hub/Returns?hub=GUL", "/Hub/Returns?hub=GUL", ("riderId", $"{rider.Id}"), ("codes", first), ("codes", second));
        Assert.Contains("is out with the rider: 2 parcels back to the merchant", sent);
        var number = await QueryAsync("onedrop", db => db.ReturnLists.Where(l => l.RiderId == rider.Id).Select(l => l.Number).SingleAsync(Cancel));
        var out1 = await ParcelAsync(first);
        Assert.Equal((ParcelStatus.Returning, (long?)rider.Id, (long?)null), (out1.Status, out1.RiderId, out1.CurrentHubId));

        var owner = await SignInAsync("onedrop", shop.Email);
        Assert.Contains($"On its way to you with a rider, on {number}", await owner.PageAsync("/Merchant/Returns"));
        Assert.Contains("On the way to you.", await owner.PageAsync($"/Merchant/Return/{number}"));

        var door = await SignInAsync("onedrop", rider.Email);
        Assert.Contains(number, await door.PageAsync("/Rider"));
        Assert.Contains($"Handed back to the merchant: {number}", await door.SubmitAsync($"/Rider/Return/{number}", $"/Rider/Return/{number}?handler=HandOver"));
        var back = await ParcelAsync(first);
        Assert.Equal((ParcelStatus.Returned, (long?)null), (back.Status, back.RiderId));
        var charged = await QueryAsync("onedrop", db => db.LedgerEntries.Where(e => e.ParcelId == back.Id).SumAsync(e => e.Amount, Cancel));
        Assert.Equal(-(back.DeliveryCharge + back.ReturnCharge), charged);

        Assert.Contains("1 return list was handed over to you", await owner.PageAsync("/Merchant"));
        Assert.Contains("Did these parcels arrive?", await owner.PageAsync($"/Merchant/Return/{number}"));
        var confirmed = await owner.SubmitAsync($"/Merchant/Return/{number}", $"/Merchant/Return/{number}?handler=Confirm", ("note", "One box was opened"));
        Assert.Contains($"Return list {number} is confirmed", confirmed);
        Assert.Contains("You confirmed these parcels arrived", confirmed);
        var list = await QueryAsync("onedrop", db => db.ReturnLists.AsNoTracking().SingleAsync(l => l.Number == number, Cancel));
        Assert.Equal((ReturnListStatus.Confirmed, "One box was opened"), (list.Status, list.Note));
        Assert.NotNull(list.ConfirmedById);
        Assert.Contains("One box was opened", await hub.PageAsync("/Hub/Returns?hub=GUL"));

        var again = await owner.SubmitAsync($"/Merchant/Return/{number}", $"/Merchant/Return/{number}?handler=Confirm", ("note", ""));
        Assert.Contains("is already confirmed", again);
    }

    [Fact]
    public async Task Returns_a_rider_could_not_hand_over_come_back_to_the_hub_and_can_go_out_again()
    {
        WebAppFactory.RequireDatabase();
        var shop = await NewMerchantAsync(area: "Banani");
        var rider = await NewRiderAsync("GUL");
        var code = await ReturningAsync(shop, "Gulshan 2");
        var hub = await SignInAsync("onedrop", "hub@onedrop.test");
        await hub.SubmitAsync("/Hub/Returns?hub=GUL", "/Hub/Returns?hub=GUL", ("riderId", $"{rider.Id}"), ("codes", code));
        var number = await QueryAsync("onedrop", db => db.ReturnLists.Where(l => l.RiderId == rider.Id).Select(l => l.Number).SingleAsync(Cancel));

        var door = await SignInAsync("onedrop", rider.Email);
        Assert.Contains("Say why", await door.SubmitAsync($"/Rider/Return/{number}", $"/Rider/Return/{number}?handler=Miss", ("reason", " ")));
        Assert.Contains("Not handed over", await door.SubmitAsync($"/Rider/Return/{number}", $"/Rider/Return/{number}?handler=Miss", ("reason", "Shop closed")));
        var missed = await ParcelAsync(code);
        Assert.Equal((ParcelStatus.Returning, (long?)rider.Id), (missed.Status, missed.RiderId));
        var owner = await SignInAsync("onedrop", shop.Email);
        Assert.Contains("could not hand these over", await owner.PageAsync($"/Merchant/Return/{number}"));

        // Scanned in at the hub, it waits there to go back again, on a new list
        await hub.SubmitAsync("/Hub/Scan?hub=GUL", "/Hub/Scan?mode=Receive&hub=GUL", ("Code", code));
        Assert.Equal(ParcelStatus.Returning, (await ParcelAsync(code)).Status);
        Assert.NotNull((await ParcelAsync(code)).CurrentHubId);
        var again = await hub.SubmitAsync("/Hub/Returns?hub=GUL", "/Hub/Returns?hub=GUL", ("riderId", $"{rider.Id}"), ("codes", code));
        Assert.Contains("is out with the rider: 1 parcel back to the merchant", again);
        Assert.Equal(2, await QueryAsync("onedrop", db => db.ReturnLists.CountAsync(l => l.RiderId == rider.Id, Cancel)));
    }

    /// <summary>A parcel of <paramref name="shop"/> to <paramref name="area"/>, dropped at its hub and asked back: returning, waiting there.</summary>
    private async Task<string> ReturningAsync(TestMerchant shop, string area)
    {
        var code = await BookAsync(shop.ApiKey, area: area, cod: 900);
        await QueryAsync("onedrop", async db =>
        {
            var parcel = await db.Parcels.SingleAsync(p => p.TrackingCode == code, Cancel);
            parcel.ReceiveAt(parcel.PickupHubId, Today);
            parcel.RequestReturn("The shop asked for it back");

            return await db.SaveChangesAsync(Cancel);
        });

        return code;
    }
}
