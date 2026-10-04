using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Application.Abstractions;
using Application.Notifications;
using Domain.Notifications;
using Infrastructure.MultiTenancy;
using Infrastructure.Persistence;

namespace Integration.Tests;

/// <summary>
/// Task 5.2: an operator's admin sees the texts and webhooks that have not gone out and sends a given-up one again;
/// another operator sees none of them. The test host runs no sender, so a message stays where the test puts it.
/// </summary>
public partial class FailedMessagesTests(WebAppFactory factory)
{
    private const string Password = "OneDrop#2026";
    private const string Page = "/Admin/Messages";

    [Fact]
    public async Task An_admin_sees_what_failed_and_sends_a_given_up_text_again()
    {
        WebAppFactory.RequireDatabase();
        var phone = "015" + Random.Shared.Next(0, 100_000_000).ToString("D8");
        var givenUp = await CreateAsync(phone);
        var retrying = await CreateAsync(phone);
        var givenUpId = await FailAsync(givenUp, OutboxMessage.MaxAttempts);
        await FailAsync(retrying, 1);
        var dhaka = await SignInAsync("dhaka", "admin@dhaka.onedrop.test");
        var chattogram = await SignInAsync("chattogram", "admin@chattogram.onedrop.test");

        // Both listed with what they were about and for whom; only the given-up one can be sent again
        var page = WebUtility.HtmlDecode(await dhaka.GetStringAsync(Page, Cancel));
        Assert.Contains($"Order {givenUp} placed", page);
        Assert.Contains($"Order {retrying} placed", page);
        Assert.Contains("+88" + phone, page);
        Assert.Contains("Gateway down", page);
        Assert.Contains("Given up", page);
        Assert.Contains("Retrying", page);
        Assert.Contains($"name=\"id\" value=\"{givenUpId}\"", page);

        // Another operator sees none of it and cannot send it
        var other = await chattogram.GetStringAsync(Page, Cancel);
        Assert.DoesNotContain(givenUp, other);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAgainAsync(chattogram, givenUpId)).StatusCode);
        Assert.Equal(OutboxStatus.Failed, (await MessageAsync(givenUpId)).Status);

        // Sent again: due at once with fresh attempts, and no longer listed as given up
        var again = await SendAgainAsync(dhaka, givenUpId);
        Assert.Equal(HttpStatusCode.Redirect, again.StatusCode);
        var message = await MessageAsync(givenUpId);
        Assert.Equal((OutboxStatus.Pending, 0, null), (message.Status, message.Attempts, message.NextAttemptOn));
        Assert.DoesNotContain($"Order {givenUp} placed", WebUtility.HtmlDecode(await dhaka.GetStringAsync(Page, Cancel)));

        // Shops and hub staff do not get the page
        var hub = await SignInAsync("dhaka", "hub@dhaka.onedrop.test");
        Assert.NotEqual(HttpStatusCode.OK, (await hub.GetAsync(Page, Cancel)).StatusCode);
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private async Task<string> CreateAsync(string phone)
    {
        var order = new
        {
            Customer = new { Name = "Failed Message Customer", Phone = phone },
            Address = new { Area = "Mirpur 10", Line1 = "House 3, Road 3" },
            Packages = new[] { new { Description = "Parcel", WeightGrams = 300 } },
            CodAmount = 0,
            Speed = "combine"
        };
        var response = await factory.ClientFor(WebAppFactory.DhakaFashion).PostAsJsonAsync("/api/v1/orders", order, Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<Created>(Cancel))!.Number;
    }

    /// <summary>Fails the order's "placed" text as often as asked, as the sender would; returns its id.</summary>
    private async Task<long> FailAsync(string number, int times)
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var orderId = await db.Orders.Where(o => o.Number == number).Select(o => o.Id).SingleAsync(Cancel);
        var payload = JsonSerializer.Serialize(new OrderPlacedMessage(orderId));
        var message = await db.OutboxMessages.SingleAsync(
            m => m.Type == nameof(OrderPlacedMessage) && m.Payload == payload,
            Cancel);
        for (var attempt = 0; attempt < times; attempt++)
        {
            message.MarkFailed("Gateway down", DateTime.UtcNow);
        }

        await db.SaveChangesAsync(Cancel);

        return message.Id;
    }

    private async Task<OutboxMessage> MessageAsync(long id)
    {
        await using var scope = await ScopeAsync();

        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().OutboxMessages
            .AsNoTracking()
            .SingleAsync(m => m.Id == id, Cancel);
    }

    private static async Task<HttpResponseMessage> SendAgainAsync(HttpClient client, long id)
    {
        var token = Token().Match(await client.GetStringAsync(Page, Cancel)).Groups[1].Value;
        var form = new FormUrlEncodedContent(
        [
            KeyValuePair.Create("id", id.ToString()),
            KeyValuePair.Create("__RequestVerificationToken", token)
        ]);

        return await client.PostAsync($"{Page}?handler=SendAgain", form, Cancel);
    }

    private async Task<HttpClient> SignInAsync(string slug, string email)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri($"http://{slug}.localhost"),
            AllowAutoRedirect = false
        });
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

    private async Task<AsyncServiceScope> ScopeAsync()
    {
        var scope = factory.Services.CreateAsyncScope();
        var tenant = await factory.Services.GetRequiredService<ITenantCatalog>().FindBySlugAsync("dhaka", Cancel);
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(tenant!);

        return scope;
    }

    [GeneratedRegex("""name="__RequestVerificationToken" type="hidden" value="([^"]+)""")]
    private static partial Regex Token();

    private sealed record Created(string Number);
}
