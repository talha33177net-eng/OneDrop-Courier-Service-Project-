using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Integration.Tests;

/// <summary>Task 5.1: a shop makes and revokes its own API keys; no other shop sees or touches them.</summary>
public partial class MerchantApiKeysTests(WebAppFactory factory)
{
    private const string Password = "OneDrop#2026";
    private const string Page = "/Merchant/ApiKeys";

    [Fact]
    public async Task A_shop_makes_a_key_that_works_at_once_and_stops_working_when_revoked()
    {
        WebAppFactory.RequireDatabase();
        var beauty = await SignInAsync("beauty@dhaka.onedrop.test");
        var gadget = await SignInAsync("gadget@dhaka.onedrop.test");
        var name = $"Test site {Guid.NewGuid():N}"[..20];

        Assert.Contains("Give the key a name", await PostAsync(beauty, "Issue", ("Name", "  ")));

        // The key is shown once, on the answer, and works straight away
        var answer = await PostAsync(beauty, "Issue", ("Name", name));
        var plaintext = Key().Match(answer).Value;
        Assert.NotEmpty(plaintext);
        var prefix = plaintext.Split('_')[1];
        var client = factory.ClientFor(plaintext);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/areas", Cancel)).StatusCode);
        var listed = await PageAsync(beauty);
        Assert.Contains(name, listed);
        Assert.Contains($"od_{prefix}_…", listed);
        Assert.DoesNotContain(plaintext, listed);

        // Another shop neither sees it nor can revoke it
        Assert.DoesNotContain(prefix, await PageAsync(gadget));
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(gadget, "Revoke", ("prefix", prefix))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/areas", Cancel)).StatusCode);

        // Revoked: refused on the next call
        Assert.Contains($"Key od_{prefix}_… is revoked", await PostAsync(beauty, "Revoke", ("prefix", prefix)));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/areas", Cancel)).StatusCode);
        Assert.Contains("Revoked", await PageAsync(beauty));

        var anonymous = await Client().GetAsync(Page, Cancel);
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static async Task<string> PageAsync(HttpClient client)
    {
        return WebUtility.HtmlDecode(await client.GetStringAsync(Page, Cancel));
    }

    /// <summary>Posts one of the page's forms and returns the page it shows, following a redirect.</summary>
    private static async Task<string> PostAsync(HttpClient client, string handler, params (string Name, string Value)[] fields)
    {
        var response = await SendAsync(client, handler, fields);
        if (response.StatusCode == HttpStatusCode.Redirect)
        {
            return WebUtility.HtmlDecode(await client.GetStringAsync(response.Headers.Location!.OriginalString, Cancel));
        }

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Cancel));
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        string handler,
        params (string Name, string Value)[] fields)
    {
        var token = Token().Match(await client.GetStringAsync(Page, Cancel)).Groups[1].Value;
        var form = new FormUrlEncodedContent(
        [
            .. fields.Select(field => KeyValuePair.Create(field.Name, field.Value)),
            KeyValuePair.Create("__RequestVerificationToken", token)
        ]);

        return await client.PostAsync($"{Page}?handler={handler}", form, Cancel);
    }

    /// <summary>Signs a demo shop in on Dhaka's subdomain. Keeps the sign-in cookie.</summary>
    private async Task<HttpClient> SignInAsync(string email)
    {
        var client = Client();
        var token = Token().Match(await client.GetStringAsync("/Account/Login", Cancel)).Groups[1].Value;
        var fields = new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = Password,
            ["__RequestVerificationToken"] = token
        };

        var signedIn = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(fields), Cancel);
        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);

        return client;
    }

    private HttpClient Client()
    {
        return factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://dhaka.localhost"),
            AllowAutoRedirect = false
        });
    }

    [GeneratedRegex("""name="__RequestVerificationToken" type="hidden" value="([^"]+)""")]
    private static partial Regex Token();

    [GeneratedRegex("od_[a-z0-9]{12}_[A-Za-z0-9]{32}")]
    private static partial Regex Key();
}
