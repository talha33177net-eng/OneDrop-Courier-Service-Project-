using System.Net.Http.Json;
using System.Text.Json;
using Domain.Parcels;

namespace Integration.Tests;

/// <summary>
/// The tabs of the parcel lists beyond the stages: partly delivered on its own, and the problems a hub flags for the
/// courier to look at, which the merchant sees and which keep a parcel off a rider until cleared.
/// </summary>
public class ParcelTabsTests(WebAppFactory factory) : AppTests(factory)
{
    [Fact]
    public async Task A_parcel_a_hub_flags_shows_in_its_tab_for_its_merchant_only_and_stays_off_a_rider_until_cleared()
    {
        WebAppFactory.RequireDatabase();
        var shop = await NewMerchantAsync(area: "Banani");
        var otherShop = await NewMerchantAsync(area: "Banani");
        var rider = await NewRiderAsync("GUL");
        var code = await BookAsync(shop.ApiKey, area: "Gulshan 2");
        var hub = await SignInAsync("onedrop", "hub@onedrop.test");

        // Still at the merchant: the courier has nothing to look at yet
        var early = await hub.SubmitAsync($"/Hub/Parcel/{code}", $"/Hub/Parcel/{code}?handler=Flag", ("issue", "InReview"), ("note", "Check it"));
        Assert.Contains("has not been picked up yet", early);

        await hub.SubmitAsync("/Hub/Scan?hub=GUL", "/Hub/Scan?mode=Receive&hub=GUL", ("Code", code));
        var flagged = await hub.SubmitAsync($"/Hub/Parcel/{code}", $"/Hub/Parcel/{code}?handler=Flag", ("issue", "InReview"), ("note", "Address unclear"));
        Assert.Contains($"{code} is flagged in review", flagged);
        Assert.Contains("Clear the flag", flagged);

        var owner = await SignInAsync("onedrop", shop.Email);
        Assert.Contains(code, await owner.PageAsync("/Merchant/Parcels?tab=InReview"));
        Assert.DoesNotContain(code, ListOf(await owner.PageAsync("/Merchant/Parcels?tab=Exceptional")));
        var page = await owner.PageAsync($"/Merchant/Parcel/{code}");
        Assert.Contains("In review:", page);
        Assert.Contains("Address unclear", page);
        Assert.Contains("1 parcel is flagged by the courier", await owner.PageAsync("/Merchant"));
        var api = await Factory.ClientFor(shop.ApiKey).GetFromJsonAsync<JsonElement>($"/api/v1/parcels/{code}", Json, Cancel);
        Assert.Equal(("inReview", "Address unclear"), (api.GetProperty("issue").GetString(), api.GetProperty("issueNote").GetString()));
        var other = await SignInAsync("onedrop", otherShop.Email);
        Assert.DoesNotContain(code, await other.PageAsync("/Merchant/Parcels?tab=InReview"));
        Assert.DoesNotContain("flagged by the courier", await other.PageAsync("/Merchant"));

        // The flag keeps it at the hub: the assign page offers no box for it, and a forced hand-over is refused
        Assert.Contains("stays here until the flag is cleared", await hub.PageAsync("/Hub/Assign?hub=GUL"));
        var refused = await hub.SubmitAsync("/Hub/Assign?hub=GUL", "/Hub/Assign?hub=GUL", ("riderId", $"{rider.Id}"), ("codes", code));
        Assert.Contains("Clear the flag on its page before it goes out", refused);
        Assert.Equal(ParcelStatus.AtHub, (await ParcelAsync(code)).Status);

        var cleared = await hub.SubmitAsync($"/Hub/Parcel/{code}", $"/Hub/Parcel/{code}?handler=ClearFlag", ("note", "Address confirmed with the customer"));
        Assert.Contains($"The flag on {code} is cleared", cleared);
        Assert.Contains("Review finished: Address confirmed with the customer", await owner.PageAsync($"/Merchant/Parcel/{code}"));
        Assert.DoesNotContain(code, await owner.PageAsync("/Merchant/Parcels?tab=InReview"));
        Assert.Contains("1 parcel handed over", await hub.SubmitAsync("/Hub/Assign?hub=GUL", "/Hub/Assign?hub=GUL", ("riderId", $"{rider.Id}"), ("codes", code)));
        var parcel = await ParcelAsync(code);
        Assert.Equal((ParcelStatus.OutForDelivery, null), (parcel.Status, parcel.Issue));
    }

    [Fact]
    public async Task A_parcel_an_admin_flags_exceptional_is_listed_for_the_courier_and_its_merchant()
    {
        WebAppFactory.RequireDatabase();
        var shop = await NewMerchantAsync(area: "Banani");
        var code = await BookAsync(shop.ApiKey, area: "Gulshan 2");
        var admin = await SignInAsync("onedrop", "admin@onedrop.test");
        await admin.SubmitAsync("/Hub/Scan?hub=GUL", "/Hub/Scan?mode=Receive&hub=GUL", ("Code", code));

        await admin.SubmitAsync($"/Hub/Parcel/{code}", $"/Hub/Parcel/{code}?handler=Flag", ("issue", "Exceptional"), ("note", "Box crushed in the van"));

        Assert.Contains(code, await admin.PageAsync($"/Hub/Parcels?tab=Exceptional&merchantId={shop.Id}"));
        Assert.Contains("flagged to settle", await admin.PageAsync("/Admin"));
        var owner = await SignInAsync("onedrop", shop.Email);
        Assert.Contains(code, await owner.PageAsync("/Merchant/Parcels?tab=Exceptional"));
        Assert.Contains("Exceptional:", await owner.PageAsync($"/Merchant/Parcel/{code}"));
        Assert.Contains("(1 exceptional)", await owner.PageAsync("/Merchant"));
    }

    [Fact]
    public async Task Partly_delivered_parcels_have_a_tab_of_their_own_apart_from_the_delivered_ones()
    {
        WebAppFactory.RequireDatabase();
        var shop = await NewMerchantAsync(area: "Banani");
        var rider = await NewRiderAsync("GUL");
        var whole = await BookAsync(shop.ApiKey, area: "Gulshan 2", cod: 1000);
        var part = await BookAsync(shop.ApiKey, area: "Gulshan 2", cod: 1000);
        var hub = await SignInAsync("onedrop", "hub@onedrop.test");
        foreach (var code in new[] { whole, part })
        {
            await hub.SubmitAsync("/Hub/Scan?hub=GUL", "/Hub/Scan?mode=Receive&hub=GUL", ("Code", code));
        }

        await hub.SubmitAsync("/Hub/Assign?hub=GUL", "/Hub/Assign?hub=GUL", ("riderId", $"{rider.Id}"), ("codes", whole), ("codes", part));
        var door = await SignInAsync("onedrop", rider.Email);
        await door.SubmitAsync($"/Rider/Delivery/{whole}", $"/Rider/Delivery/{whole}?handler=Deliver", ("collected", "1000"), ("reason", ""));
        await door.SubmitAsync($"/Rider/Delivery/{part}", $"/Rider/Delivery/{part}?handler=Deliver", ("collected", "600"), ("reason", "Kept one of two"));

        var owner = await SignInAsync("onedrop", shop.Email);
        var partly = ListOf(await owner.PageAsync("/Merchant/Parcels?tab=PartlyDelivered"));
        var delivered = ListOf(await owner.PageAsync("/Merchant/Parcels?tab=Delivered"));
        Assert.Contains(part, partly);
        Assert.DoesNotContain(whole, partly);
        Assert.Contains(whole, delivered);
        Assert.DoesNotContain(part, delivered);
        Assert.Matches(@"Partly delivered\s*<span class=""tab-count[^""]*"">1</span>", partly);
        Assert.Matches(@"Delivered\s*<span class=""tab-count[^""]*"">1</span>", delivered);
    }

    /// <summary>The parcel list of a page from its tabs on, leaving out the bell above it, which names parcels too.</summary>
    private static string ListOf(string page)
    {
        return page[page.IndexOf("class=\"tabs\"", StringComparison.Ordinal)..];
    }
}
