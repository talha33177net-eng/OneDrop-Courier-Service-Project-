using Microsoft.EntityFrameworkCore;
using Domain.Parcels;

namespace Integration.Tests;

/// <summary>A merchant asking to cancel a parcel on its way or change its cash, and the courier's admins answering.</summary>
public class ParcelRequestsTests(WebAppFactory factory) : AppTests(factory)
{
    [Fact]
    public async Task An_approved_change_of_cash_changes_the_parcel_and_the_merchant_sees_the_answer()
    {
        WebAppFactory.RequireDatabase();
        var shop = await NewMerchantAsync(area: "Banani");
        var code = await AtHubAsync(shop, cod: 1250);
        var owner = await SignInAsync("onedrop", shop.Email);

        var asked = await owner.SubmitAsync($"/Merchant/Parcel/{code}", $"/Merchant/Parcel/{code}?handler=Request", ("kind", "ChangeCod"), ("amount", "900"), ("reason", "A discount for the customer"));
        Assert.Contains($"We asked the courier about {code}", asked);
        Assert.Contains("You asked the courier on", asked);
        Assert.Contains("Waiting for an answer", await owner.PageAsync("/Merchant/Requests"));

        var admin = await SignInAsync("onedrop", "admin@onedrop.test");
        var waiting = await admin.PageAsync("/Admin/Requests");
        Assert.Contains(code, waiting);
        Assert.Contains(shop.Name, waiting);
        Assert.Contains("merchant request", await admin.PageAsync("/Admin"));
        Assert.Contains("The merchant asked:", await admin.PageAsync($"/Hub/Parcel/{code}"));
        Assert.Contains($"Approved: {code} is changed", await admin.SubmitAsync("/Admin/Requests", $"/Admin/Requests?handler=Approve&code={code}", ("answer", "")));

        Assert.Equal(900, (await ParcelAsync(code)).CodAmount);
        var page = await owner.PageAsync($"/Merchant/Parcel/{code}");
        Assert.Contains("The courier approved your request", page);
        Assert.Contains("Cash on delivery changed from ৳1,250 to ৳900", page);
        var other = await SignInAsync("onedrop", (await NewMerchantAsync(area: "Banani")).Email);
        Assert.DoesNotContain(code, await other.PageAsync("/Merchant/Requests"));
    }

    [Fact]
    public async Task A_refusal_needs_a_reason_and_leaves_the_parcel_and_an_approved_cancellation_brings_it_back()
    {
        WebAppFactory.RequireDatabase();
        var shop = await NewMerchantAsync(area: "Banani");
        var code = await AtHubAsync(shop, cod: 700);
        var owner = await SignInAsync("onedrop", shop.Email);
        var admin = await SignInAsync("onedrop", "admin@onedrop.test");

        await owner.SubmitAsync($"/Merchant/Parcel/{code}", $"/Merchant/Parcel/{code}?handler=Request", ("kind", "Cancel"), ("reason", "Customer cancelled the order"));
        var twice = await owner.SubmitAsync($"/Merchant/Parcel/{code}", $"/Merchant/Parcel/{code}?handler=Request", ("kind", "Cancel"), ("reason", "Again"));
        Assert.Contains($"You already asked about {code}", twice);
        Assert.Contains("Say why the request is refused", await admin.SubmitAsync("/Admin/Requests", $"/Admin/Requests?handler=Refuse&code={code}", ("answer", " ")));
        Assert.Contains("Refused", await admin.SubmitAsync("/Admin/Requests", $"/Admin/Requests?handler=Refuse&code={code}", ("answer", "It is already on the van")));

        Assert.Equal(ParcelStatus.AtHub, (await ParcelAsync(code)).Status);
        var refused = await owner.PageAsync($"/Merchant/Parcel/{code}");
        Assert.Contains("The courier refused your request", refused);
        Assert.Contains("It is already on the van", refused);

        await owner.SubmitAsync($"/Merchant/Parcel/{code}", $"/Merchant/Parcel/{code}?handler=Request", ("kind", "Cancel"), ("reason", "Customer cancelled the order"));
        await admin.SubmitAsync("/Admin/Requests", $"/Admin/Requests?handler=Approve&code={code}", ("answer", "Bringing it back"));
        var back = await ParcelAsync(code);
        Assert.Equal((ParcelStatus.Returning, "Cancelled by the merchant: Customer cancelled the order"), (back.Status, back.ReturnReason));
        Assert.Equal(2, await QueryAsync("onedrop", db => db.ParcelRequests.CountAsync(r => r.ParcelId == back.Id, Cancel)));
    }

    [Fact]
    public async Task A_parcel_not_picked_up_yet_is_not_asked_about_the_merchant_changes_it_itself()
    {
        WebAppFactory.RequireDatabase();
        var shop = await NewMerchantAsync(area: "Banani");
        var code = await BookAsync(shop.ApiKey, area: "Gulshan 2");
        var owner = await SignInAsync("onedrop", shop.Email);

        var refused = await owner.SubmitAsync($"/Merchant/Parcel/{code}", $"/Merchant/Parcel/{code}?handler=Request", ("kind", "Cancel"), ("reason", "Not needed"));

        Assert.Contains("has not been picked up yet: edit or cancel it yourself", refused);
        Assert.Equal(0, await QueryAsync("onedrop", db => db.ParcelRequests.CountAsync(r => r.MerchantId == shop.Id, Cancel)));
    }

    /// <summary>A parcel of <paramref name="shop"/> dropped off at Gulshan hub, which delivers it.</summary>
    private async Task<string> AtHubAsync(TestMerchant shop, decimal cod)
    {
        var code = await BookAsync(shop.ApiKey, area: "Gulshan 2", cod: cod);
        await QueryAsync("onedrop", async db =>
        {
            var parcel = await db.Parcels.SingleAsync(p => p.TrackingCode == code, Cancel);
            parcel.ReceiveAt(parcel.DeliveryHubId, Today);

            return await db.SaveChangesAsync(Cancel);
        });

        return code;
    }
}
