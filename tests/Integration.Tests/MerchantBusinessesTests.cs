using System.Net;
using Microsoft.EntityFrameworkCore;
using Domain.Merchants;

namespace Integration.Tests;

/// <summary>Several businesses in one merchant account: each its own parcels and money, one login, shared approval.</summary>
public class MerchantBusinessesTests(WebAppFactory factory) : AppTests(factory)
{
    [Fact]
    public async Task A_merchant_adds_a_business_works_in_it_and_each_business_keeps_its_own_parcels()
    {
        WebAppFactory.RequireDatabase();
        var shop = await NewMerchantAsync();
        var signIn = await SignInCookieAsync("onedrop", shop.Email);
        var name = $"Second shop {Guid.NewGuid():N}"[..24];
        var areaId = await AreaIdAsync("Dhanmondi");

        var added = await Visit("onedrop", signIn).PostFormAsync(
            "/Merchant/NewBusiness",
            "/Merchant/NewBusiness",
            ("Input.Name", name),
            ("Input.Phone", NewPhone()),
            ("Input.Address", "Road 2, Dhanmondi"),
            ("Input.PickupAreaId", $"{areaId}"),
            ("Input.PickupAddress", "Road 2, Dhanmondi"));
        Assert.Equal(HttpStatusCode.Redirect, added.StatusCode);
        var business = await QueryAsync("onedrop", db => db.Merchants.SingleAsync(m => m.Name == name, Cancel));
        Assert.Equal(shop.Id, business.MainMerchantId);
        Assert.Equal(MerchantStatus.Active, business.Status);

        // The renewed sign-in and the business cookie come back together; the browser keeps both
        var working = Merge(signIn, Visitor.Cookies(added));
        var inBusiness = Visit("onedrop", working);
        Assert.Contains($"Hello, {name}", await inBusiness.PageAsync("/Merchant"));
        var list = await inBusiness.PageAsync("/Merchant/Businesses");
        Assert.Contains(shop.Name, list);
        Assert.Contains("1 of 10 businesses added", list);

        Assert.Contains("is booked for", await inBusiness.SubmitAsync("/Merchant/NewParcel", "/Merchant/NewParcel", Booking(areaId)));
        var code = await QueryAsync("onedrop", db => db.Parcels.Where(p => p.MerchantId == business.Id).Select(p => p.TrackingCode).SingleAsync(Cancel));
        Assert.Contains(code, await inBusiness.PageAsync("/Merchant/Parcels"));

        // The main profile, which the same login opens without the cookie, does not have the other business's parcel
        var main = Visit("onedrop", Merge(working, $"{BusinessCookieName(working)}="));
        Assert.Contains($"Hello, {shop.Name}", await main.PageAsync("/Merchant"));
        Assert.DoesNotContain(code, await main.PageAsync("/Merchant/Parcels"));

        // A payout account set while working in the business is the whole account's
        Assert.Contains("payout account is saved", await inBusiness.SubmitAsync("/Merchant/Settings", "/Merchant/Settings?handler=Payout", ("method", "Nagad"), ("payoutAccount", "01911222444"), ("accountName", "Test Owner")));
        var accounts = await QueryAsync("onedrop", db => db.Merchants.Where(m => m.Id == shop.Id || m.Id == business.Id).Select(m => m.PayoutAccount).Distinct().ToListAsync(Cancel));
        Assert.Equal(["+8801911222444"], accounts);
    }

    [Fact]
    public async Task A_business_cookie_naming_someone_else_s_business_changes_nothing_and_suspension_covers_the_whole_account()
    {
        WebAppFactory.RequireDatabase();
        var shop = await NewMerchantAsync();
        var stranger = await NewMerchantAsync();
        var signIn = await SignInCookieAsync("onedrop", shop.Email);
        var added = await Visit("onedrop", signIn).PostFormAsync(
            "/Merchant/NewBusiness",
            "/Merchant/NewBusiness",
            ("Input.Name", $"Side shop {Guid.NewGuid():N}"[..22]),
            ("Input.Phone", NewPhone()),
            ("Input.Address", "Road 5, Mirpur"),
            ("Input.PickupAreaId", $"{await AreaIdAsync("Mirpur 10")}"),
            ("Input.PickupAddress", "Road 5, Mirpur"));
        var working = Merge(signIn, Visitor.Cookies(added));

        var forged = Visit("onedrop", Merge(working, $"{BusinessCookieName(working)}={stranger.Id}"));
        var page = await forged.PageAsync("/Merchant");
        Assert.Contains($"Hello, {shop.Name}", page);
        Assert.DoesNotContain(stranger.Name, page);

        var admin = await SignInAsync("onedrop", "admin@onedrop.test");
        await admin.SubmitAsync($"/Admin/Merchant/{shop.Id}", $"/Admin/Merchant/{shop.Id}?handler=Suspend");
        var statuses = await QueryAsync("onedrop", db => db.Merchants.Where(m => m.Id == shop.Id || m.MainMerchantId == shop.Id).Select(m => m.Status).ToListAsync(Cancel));
        Assert.Equal(2, statuses.Count);
        Assert.All(statuses, status => Assert.Equal(MerchantStatus.Suspended, status));
    }

    /// <summary>The cookies a browser would hold after <paramref name="update"/>: same names replaced, empty values dropped.</summary>
    private static string Merge(string cookies, string update)
    {
        var jar = new Dictionary<string, string>();
        foreach (var pair in $"{cookies}; {update}".Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var at = pair.IndexOf('=');
            jar[pair[..at]] = pair[(at + 1)..];
        }

        return string.Join("; ", jar.Where(cookie => cookie.Value.Length > 0).Select(cookie => $"{cookie.Key}={cookie.Value}"));
    }

    private static string BusinessCookieName(string cookies)
    {
        return cookies.Split("; ").Select(pair => pair.Split('=')[0]).Single(cookie => cookie.StartsWith("business-", StringComparison.Ordinal));
    }

    private Task<long> AreaIdAsync(string area)
    {
        return QueryAsync("onedrop", db => db.Areas.Where(a => a.Name == area).Select(a => a.Id).SingleAsync(Cancel));
    }

    private static (string, string)[] Booking(long areaId)
    {
        return
        [
            ("Input.RecipientName", "Ayesha Siddiqua"),
            ("Input.RecipientPhone", "01811000101"),
            ("Input.RecipientAddress", "House 9, Road 3"),
            ("Input.AreaId", $"{areaId}"),
            ("Input.CodAmount", "1500"),
            ("Input.WeightKg", "0.5"),
            ("Input.ItemDescription", "Saree"),
            ("Input.MerchantReference", "FB-1"),
            ("FormKey", Guid.NewGuid().ToString("N"))
        ];
    }
}
