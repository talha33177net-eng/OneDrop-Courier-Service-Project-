using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace Integration.Tests;

/// <summary>
/// Moderators: people who work in a merchant account with a sign-in of their own. They see what the owner ticked and
/// nothing else, they never manage other moderators, and a stopped one is shut out.
/// </summary>
public partial class ModeratorsTests(WebAppFactory factory) : AppTests(factory)
{
    [Fact]
    public async Task A_moderator_opens_only_what_the_owner_ticked_and_is_shut_out_once_stopped()
    {
        WebAppFactory.RequireDatabase();
        var shop = await NewMerchantAsync();
        var code = await BookAsync(shop.ApiKey);
        var owner = await SignInAsync("onedrop", shop.Email);
        var email = $"{Guid.NewGuid():N}@staff.test";

        var added = await owner.PostFormAsync(
            "/Merchant/Moderators",
            "/Merchant/Moderators?handler=Add",
            ("Name", "Nusrat Jahan"),
            ("Email", email),
            ("Phone", NewPhone()),
            ("Permissions", "Dashboard"),
            ("Permissions", "Parcels"));
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        var answer = WebUtility.HtmlDecode(await added.Content.ReadAsStringAsync(Cancel));
        var password = Password().Match(answer).Groups[1].Value;
        Assert.True(password.Length > 0, $"No password was shown. The page said: {Problem().Match(answer).Groups[1].Value}");

        var staff = Visit("onedrop", await SignInCookieAsync("onedrop", email, password));
        var dashboard = await staff.PageAsync("/Merchant");
        Assert.Contains(shop.Name, dashboard);
        Assert.Contains(code, await staff.PageAsync("/Merchant/Parcels"));

        // Not ticked: payments, booking, the settings and the moderators themselves, which are the owner's alone
        await RefusedAsync(staff, "/Merchant/Payments");
        await RefusedAsync(staff, "/Merchant/NewParcel");
        await RefusedAsync(staff, "/Merchant/Settings");
        await RefusedAsync(staff, "/Merchant/Moderators");
        Assert.DoesNotContain("/Merchant/Moderators", dashboard);
        Assert.DoesNotContain("/Merchant/Payments", dashboard);
        Assert.Contains("Moderator", dashboard);

        var id = await QueryAsync("onedrop", db => db.Moderators.Where(m => m.AccountId == shop.Id).Select(m => m.Id).SingleAsync(Cancel));
        await owner.PostFormAsync(
            "/Merchant/Moderators",
            $"/Merchant/Moderators?handler=Permissions&id={id}",
            ("Permissions", "Dashboard"),
            ("Permissions", "Payments"));
        Assert.Contains("Payments", await staff.PageAsync("/Merchant/Payments"));
        await RefusedAsync(staff, "/Merchant/Parcels");

        var stopped = await owner.PostFormAsync("/Merchant/Moderators", $"/Merchant/Moderators?handler=Stop&id={id}");
        Assert.Equal(HttpStatusCode.Redirect, stopped.StatusCode);
        await RefusedAsync(staff, "/Merchant");
        var refusedSignIn = await Visit("onedrop").PostFormAsync(
            "/Account/Login",
            "/Account/Login",
            ("Input.Email", email),
            ("Input.Password", password));
        Assert.Equal(HttpStatusCode.OK, refusedSignIn.StatusCode);
    }

    [Fact]
    public async Task A_moderator_belongs_to_one_account_and_no_other_owner_can_see_or_stop_them()
    {
        WebAppFactory.RequireDatabase();
        var shop = await NewMerchantAsync();
        var stranger = await NewMerchantAsync();
        var owner = await SignInAsync("onedrop", shop.Email);
        var email = $"{Guid.NewGuid():N}@staff.test";
        await owner.PostFormAsync(
            "/Merchant/Moderators",
            "/Merchant/Moderators?handler=Add",
            ("Name", "Shared Staff"),
            ("Email", email),
            ("Permissions", "Dashboard"));
        var id = await QueryAsync("onedrop", db => db.Moderators.Where(m => m.AccountId == shop.Id).Select(m => m.Id).SingleAsync(Cancel));

        var other = await SignInAsync("onedrop", stranger.Email);
        var theirPage = await other.PageAsync("/Merchant/Moderators");
        Assert.DoesNotContain(email, theirPage);
        Assert.DoesNotContain("Shared Staff", theirPage);

        var stop = await other.PostFormAsync("/Merchant/Moderators", $"/Merchant/Moderators?handler=Stop&id={id}");
        Assert.Equal(HttpStatusCode.NotFound, stop.StatusCode);
        Assert.False(await QueryAsync("onedrop", db => db.Moderators.Where(m => m.Id == id).Select(m => m.Archived).SingleAsync(Cancel)));
    }

    /// <summary>A page this person may not open: the cookie scheme turns the refusal into "access denied".</summary>
    private static async Task RefusedAsync(Visitor visitor, string url)
    {
        var response = await visitor.GetAsync(url);
        Assert.True(
            response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Forbidden,
            $"{url} answered {(int)response.StatusCode}, so it was not refused");
        if (response.StatusCode == HttpStatusCode.Redirect)
        {
            Assert.Contains("AccessDenied", response.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);
        }
    }

    [GeneratedRegex("letter-spacing:1px\">([^<]+)</code>")]
    private static partial Regex Password();

    [GeneratedRegex("alert-error\" role=\"alert\">.*?<div>(.*?)</div>", RegexOptions.Singleline)]
    private static partial Regex Problem();
}
