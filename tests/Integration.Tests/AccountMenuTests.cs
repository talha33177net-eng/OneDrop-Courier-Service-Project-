using System.Net;
using System.Text.RegularExpressions;

namespace Integration.Tests;

/// <summary>What every panel carries at the top: the favourites bar a person chooses, and their account menu.</summary>
public partial class AccountMenuTests(WebAppFactory factory) : AppTests(factory)
{
    [Fact]
    public async Task A_panel_shows_the_favourites_a_person_chose_and_only_pages_of_their_own_menu()
    {
        WebAppFactory.RequireDatabase();
        var shop = await NewMerchantAsync();
        var cookie = await SignInCookieAsync("onedrop", shop.Email);

        var first = await Visit("onedrop", cookie).PageAsync("/Merchant");
        Assert.Contains("data-fav-chip=\"/Merchant/NewParcel\"", first);
        var name = FavouritesCookie().Match(first).Groups[1].Value;
        Assert.StartsWith("favourites-", name);

        var chosen = $"{name}={Uri.EscapeDataString("/Merchant/FraudCheck,/Admin,/Merchant/Pricing")}";
        var page = await Visit("onedrop", $"{cookie}; {chosen}").PageAsync("/Merchant");
        Assert.Contains("data-fav-chip=\"/Merchant/FraudCheck\"", page);
        Assert.Contains("data-fav-chip=\"/Merchant/Pricing\"", page);
        Assert.DoesNotContain("data-fav-chip=\"/Admin\"", page);
        Assert.DoesNotContain("data-fav-chip=\"/Merchant/NewParcel\"", page);
        Assert.True(page.IndexOf("data-fav-chip=\"/Merchant/FraudCheck\"") < page.IndexOf("data-fav-chip=\"/Merchant/Pricing\""), "Favourites keep the order they were added in.");

        var none = await Visit("onedrop", $"{cookie}; {name}=-").PageAsync("/Merchant");
        Assert.DoesNotContain("data-fav-chip=", none);

        var admin = await (await SignInAsync("onedrop", "admin@onedrop.test")).PageAsync("/Admin");
        Assert.Contains("data-fav-chip=\"/Admin\"", admin);
    }

    [Fact]
    public async Task A_person_changes_their_password_and_signs_in_with_the_new_one_only()
    {
        WebAppFactory.RequireDatabase();
        var shop = await NewMerchantAsync();
        var merchant = await SignInAsync("onedrop", shop.Email);
        const string next = "NewPass#2027";

        Assert.Contains("Change password", await merchant.PageAsync("/Merchant"));
        Assert.Contains(
            "That is not your current password.",
            await merchant.SubmitAsync("/Account/Password", "/Account/Password",
                ("Input.CurrentPassword", "Wrong#2026"), ("Input.NewPassword", next), ("Input.ConfirmPassword", next)));
        Assert.Contains(
            "The two new passwords are not the same.",
            await merchant.SubmitAsync("/Account/Password", "/Account/Password",
                ("Input.CurrentPassword", WebAppFactory.Password), ("Input.NewPassword", next), ("Input.ConfirmPassword", "Other#2027")));
        Assert.Contains(
            "Your password is changed",
            await merchant.SubmitAsync("/Account/Password", "/Account/Password",
                ("Input.CurrentPassword", WebAppFactory.Password), ("Input.NewPassword", next), ("Input.ConfirmPassword", next)));

        var old = await Visit("onedrop").PostFormAsync("/Account/Login", "/Account/Login", ("Input.Email", shop.Email), ("Input.Password", WebAppFactory.Password));
        Assert.Equal(HttpStatusCode.OK, old.StatusCode);
        var renewed = await Visit("onedrop").PostFormAsync("/Account/Login", "/Account/Login", ("Input.Email", shop.Email), ("Input.Password", next));
        Assert.Equal(HttpStatusCode.Redirect, renewed.StatusCode);
    }

    [GeneratedRegex("data-fav-cookie=\"([^\"]+)\"")]
    private static partial Regex FavouritesCookie();
}
