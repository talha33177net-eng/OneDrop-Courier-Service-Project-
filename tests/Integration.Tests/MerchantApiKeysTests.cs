using System.Net;
using System.Text.RegularExpressions;

namespace Integration.Tests;

/// <summary>A merchant makes and revokes its own API keys; no other merchant sees or touches them.</summary>
public partial class MerchantApiKeysTests(WebAppFactory factory) : AppTests(factory)
{
    private const string Page = "/Merchant/ApiKeys";

    [Fact]
    public async Task A_merchant_makes_a_key_that_works_at_once_and_stops_working_when_revoked()
    {
        WebAppFactory.RequireDatabase();
        var shop = await NewMerchantAsync();
        var other = await NewMerchantAsync();
        var owner = await SignInAsync("onedrop", shop.Email);
        var stranger = await SignInAsync("onedrop", other.Email);
        var name = $"Test site {Guid.NewGuid():N}"[..20];

        Assert.Contains("Give the key a name", await owner.SubmitAsync(Page, $"{Page}?handler=Issue", ("Name", "  ")));

        // The key is shown once, on the answer, and works straight away
        var answer = await owner.SubmitAsync(Page, $"{Page}?handler=Issue", ("Name", name));
        var plaintext = Key().Match(answer).Value;
        Assert.NotEmpty(plaintext);
        var prefix = plaintext.Split('_')[1];
        var client = Factory.ClientFor(plaintext);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/areas", Cancel)).StatusCode);
        var listed = await owner.PageAsync(Page);
        Assert.Contains(name, listed);
        Assert.Contains($"od_{prefix}_…", listed);
        Assert.DoesNotContain(plaintext, listed);

        // Another merchant neither sees it nor can revoke it
        Assert.DoesNotContain(prefix, await stranger.PageAsync(Page));
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostFormAsync(Page, $"{Page}?handler=Revoke", ("prefix", prefix))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/areas", Cancel)).StatusCode);

        // Revoked: refused on the next call
        Assert.Contains($"Key od_{prefix}_… is revoked", await owner.SubmitAsync(Page, $"{Page}?handler=Revoke", ("prefix", prefix)));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/areas", Cancel)).StatusCode);
        Assert.Contains("Revoked", await owner.PageAsync(Page));

        Assert.Equal(HttpStatusCode.Redirect, (await Visit("onedrop").GetAsync(Page)).StatusCode);
    }

    [GeneratedRegex("od_[a-z0-9]{12}_[A-Za-z0-9]{32}")]
    private static partial Regex Key();
}
