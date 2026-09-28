using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Application.Abstractions;
using Application.Notifications.SendOutbox;

namespace Infrastructure.Jobs;

/// <summary>
/// Runs <see cref="SendOutboxJob"/> for every active tenant every few seconds (<c>Jobs:OutboxInterval</c>), in
/// process: Hangfire's recurring jobs run at most once a minute, too slow for "your order joined" texts. Each tenant
/// runs in its own scope through <see cref="TenantJobRunner"/>, and a failing tenant is logged and skipped.
/// </summary>
public class OutboxDispatcher(
    IServiceScopeFactory scopes,
    ITenantCatalog tenants,
    TimeSpan interval,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
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
                            .RunAsync(nameof(SendOutboxJob), tenant.Id, stoppingToken);
                    }
                    catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                    {
                        logger.LogError(exception, "Sending the outbox failed for tenant {Tenant}", tenant.Slug);
                    }
                }
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(exception, "Could not list the tenants to send their outbox");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
