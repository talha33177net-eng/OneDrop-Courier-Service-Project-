using System.Net;
using Microsoft.EntityFrameworkCore;
using Domain.Merchants;

namespace Integration.Tests;

/// <summary>The picture of a business or main profile: uploaded in settings, shown in the menus, served to its own account only.</summary>
public class MerchantPictureTests(WebAppFactory factory) : AppTests(factory)
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    [Fact]
    public async Task A_merchant_uploads_a_picture_for_the_business_it_works_in_and_only_its_account_can_fetch_it()
    {
        WebAppFactory.RequireDatabase();
        var shop = await NewMerchantAsync();
        var stranger = await NewMerchantAsync();
        var shopVisit = await SignInAsync("onedrop", shop.Email);

        var uploaded = await shopVisit.UploadAsync("/Merchant/Settings", "/Merchant/Settings?handler=Picture", "picture", Png);
        Assert.Equal(HttpStatusCode.Redirect, uploaded.StatusCode);
        var stored = await QueryAsync("onedrop", db => db.MerchantPictures.Where(p => p.MerchantId == shop.Id).SingleAsync(Cancel));
        Assert.Equal("image/png", stored.ContentType);

        var page = await shopVisit.PageAsync("/Merchant/Settings");
        Assert.Contains($"/Merchant/Picture/{shop.Id}?v=", page);
        var fetched = await shopVisit.GetAsync($"/Merchant/Picture/{shop.Id}");
        Assert.Equal("image/png", fetched.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Png, await fetched.Content.ReadAsByteArrayAsync(Cancel));

        var other = await SignInAsync("onedrop", stranger.Email);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/Merchant/Picture/{shop.Id}")).StatusCode);

        // A new upload replaces the old one; removing it leaves the business without
        await shopVisit.UploadAsync("/Merchant/Settings", "/Merchant/Settings?handler=Picture", "picture", [0xFF, 0xD8, 0xFF, 0xE0, 9]);
        Assert.Equal("image/jpeg", (await QueryAsync("onedrop", db => db.MerchantPictures.Where(p => p.MerchantId == shop.Id).SingleAsync(Cancel))).ContentType);
        await shopVisit.SubmitAsync("/Merchant/Settings", "/Merchant/Settings?handler=RemovePicture");
        Assert.Equal(0, await QueryAsync("onedrop", db => db.MerchantPictures.CountAsync(p => p.MerchantId == shop.Id, Cancel)));
        Assert.Equal(HttpStatusCode.NotFound, (await shopVisit.GetAsync($"/Merchant/Picture/{shop.Id}")).StatusCode);
    }

    [Fact]
    public async Task A_file_that_is_not_a_picture_is_refused()
    {
        WebAppFactory.RequireDatabase();
        var shop = await NewMerchantAsync();
        var shopVisit = await SignInAsync("onedrop", shop.Email);

        Assert.Contains("Choose a picture", await shopVisit.SubmitAsync("/Merchant/Settings", "/Merchant/Settings?handler=Picture", ("x", "y")));
        Assert.Equal(0, await QueryAsync("onedrop", db => db.MerchantPictures.CountAsync(p => p.MerchantId == shop.Id, Cancel)));

        await shopVisit.UploadAsync("/Merchant/Settings", "/Merchant/Settings?handler=Picture", "picture", "<html>not a picture</html>"u8.ToArray());
        await shopVisit.UploadAsync("/Merchant/Settings", "/Merchant/Settings?handler=Picture", "picture", new byte[MerchantPicture.MaxBytes + 10]);
        Assert.Equal(0, await QueryAsync("onedrop", db => db.MerchantPictures.CountAsync(p => p.MerchantId == shop.Id, Cancel)));
    }
}
