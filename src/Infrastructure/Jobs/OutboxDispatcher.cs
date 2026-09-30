using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Application.Abstractions;
using Application.Notifications.SendOutbox;
using Application.Notifications.SendWebhooks;

namespace Infrastructure.Jobs;

/// <summary>
/// Runs the outbox senders, <see cref="SendOutboxJob"/> (texts) and <see cref="SendWebhooksJob"/> (shops' webhooks),
/// for every active tenant every few seconds (<c>Jobs:OutboxInterval</c>), in process: Hangfire's recurring jobs run
/// at most once a minute, too slow for "your order joined" texts. Each sender has a loop of its own, so a slow shop
/// server never holds up an SMS. Each tenant runs in its own scope through <see cref="TenantJobRunner"/>, and a
/// failing tenant is logged and skipped.
/// </summary>
public class OutboxDispatcher(
    IServiceScopeFactory scopes,
    ITenantCatalog tenants,
    TimeSpan interval,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.WhenAll(
            RunAsync(nameof(SendOutboxJob), stoppingToken),
            RunAsync(nameof(SendWebhooksJob), stoppingToken));
    }

    private async Task RunAsync(string job, CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(interval);
        do
        {
            // An exception escaping a hosted service stops the whole app, so nothing may leave this loop but shutdown
            try
            {
                foreach (var tenant in await tenants.ListAsync(stoppingToken))
                {
                    try
                    {
                        await using var scope = scopes.CreateAsyncScope();
                        await scope.ServiceProvider.GetRequiredService<TenantJobRunner>()
                            .RunAsync(job, tenant.Id, stoppingToken);
                    }
                    catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                    {
                        logger.LogError(exception, "{Job} failed for tenant {Tenant}", job, tenant.Slug);
                    }
                }
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(exception, "Could not list the tenants to run {Job}", job);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
