using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Application.Abstractions;
using Infrastructure.MultiTenancy;

namespace Infrastructure.Jobs;

/// <summary>
/// Runs an <see cref="ITenantJob"/> through Hangfire. A recurring job fans out to one job per tenant, and each of
/// those carries only the job's name and the tenant id: it sets the tenant on its own DI scope (Hangfire opens one
/// per job) before the job runs, so a failure or a slow run for one tenant never touches another.
/// </summary>
public class TenantJobRunner(
    IServiceProvider services,
    TenantContext tenantContext,
    ITenantCatalog tenants,
    TenantJobRegistry registry,
    IBackgroundJobClient jobs,
    ILogger<TenantJobRunner> logger)
{
    /// <summary>
    /// The recurring entry point: queues job <paramref name="job"/> once for every active tenant. Not retried:
    /// the next scheduled run does the same.
    /// </summary>
    [AutomaticRetry(Attempts = 0)]
    [JobDisplayName("{0} for every tenant")]
    public async Task EnqueueForEveryTenantAsync(string job, CancellationToken cancellationToken)
    {
        // An unknown name fails here once, not in one queued job per tenant
        registry.Find(job);
        foreach (var tenant in await tenants.ListAsync(cancellationToken))
        {
            jobs.Enqueue<TenantJobRunner>(runner => runner.RunAsync(job, tenant.Id, CancellationToken.None));
        }
    }

    /// <summary>Runs job <paramref name="job"/> for one tenant. Hangfire passes its own shutdown token.</summary>
    [JobDisplayName("{0} for tenant {1}")]
    public async Task RunAsync(string job, long tenantId, CancellationToken cancellationToken)
    {
        var type = registry.Find(job);
        var tenant = await tenants.FindByIdAsync(tenantId, cancellationToken);
        if (tenant is null)
        {
            // Archived after the job was queued: nothing to do, and retrying would not change that
            logger.LogWarning("Skipped {Job}: tenant {TenantId} is not active", job, tenantId);

            return;
        }

        tenantContext.Set(tenant);
        using (logger.BeginScope(new Dictionary<string, object> { ["Tenant"] = tenant.Slug }))
        {
            await ((ITenantJob)ActivatorUtilities.GetServiceOrCreateInstance(services, type)).RunAsync(cancellationToken);
        }
    }
}
