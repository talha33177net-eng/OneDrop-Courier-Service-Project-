using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Domain.Merchants;
using Infrastructure.Persistence;

namespace Web.Pages.Dev;

public sealed record ReceivedWebhook(DateTime On, string? Id, string? Timestamp, string? Signature, string Body);

/// <summary>The last 50 webhooks posted to <c>/Dev/Webhooks</c>.</summary>
public class ReceivedWebhooks
{
    private const int Keep = 50;
    private readonly ConcurrentQueue<ReceivedWebhook> received = new();

    public IReadOnlyList<ReceivedWebhook> Recent => [.. received.Reverse()];

    public void Add(ReceivedWebhook webhook)
    {
        received.Enqueue(webhook);
        while (received.Count > Keep)
        {
            received.TryDequeue(out _);
        }
    }
}

/// <summary>
/// Development only: a stand-in for a shop's website. A demo shop sets its webhook to
/// <c>http://localhost:5080/Dev/Webhooks</c>; each post is kept and shown with the shop whose secret its signature
/// checks against, as the shop's own server would check it.
/// </summary>
[IgnoreAntiforgeryToken]
public class WebhooksModel(ReceivedWebhooks log, AppDbContext db, TimeProvider time, IWebHostEnvironment environment)
    : PageModel
{
    public IReadOnlyList<(ReceivedWebhook Webhook, string? Shop)> Received { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
        {
            return NotFound();
        }

        // Every operator's shops: this page plays every shop's website at once, and exists only in Development
        await using var database = await db.AcrossTenantsAsync(cancellationToken);
        var secrets = await db.Merchants
            .IgnoreQueryFilters([QueryFilters.Tenant, QueryFilters.Merchant])
            .Where(m => m.WebhookSecret != null)
            .Select(m => new { m.Name, Secret = m.WebhookSecret! })
            .ToListAsync(cancellationToken);
        Received =
        [
            .. log.Recent.Select(webhook => (webhook, secrets
                .FirstOrDefault(shop => webhook.Id is not null && long.TryParse(webhook.Timestamp, out var timestamp) &&
                    WebhookSignature.Sign(shop.Secret, webhook.Id, timestamp, webhook.Body) == webhook.Signature)
                ?.Name))
        ];

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
        {
            return NotFound();
        }

        using var reader = new StreamReader(Request.Body);
        log.Add(new ReceivedWebhook(
            time.GetUtcNow().UtcDateTime,
            Request.Headers["webhook-id"],
            Request.Headers["webhook-timestamp"],
            Request.Headers["webhook-signature"],
            await reader.ReadToEndAsync(cancellationToken)));

        return new OkResult();
    }
}
