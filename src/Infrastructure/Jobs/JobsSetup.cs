using Hangfire;
using Hangfire.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Application.Grouping.LockDueGroups;

namespace Infrastructure.Jobs;

/// <summary>
/// Hangfire on the application database. Hangfire owns its tables (schema <c>HangFire</c>, created and upgraded by
/// Hangfire on first use); the SQL project leaves them alone because the publish never drops objects it does not
/// know. The job server runs only where <c>Jobs:Server</c> is true, so the integration tests start none.
/// </summary>
public static class JobsSetup
{
    public static IServiceCollection AddJobs(this IServiceCollection services, bool runServer)
    {
        services.AddSingleton(new TenantJobRegistry(typeof(LockDueGroupsJob)));
        services.AddScoped<TenantJobRunner>();
        // The connection string is read when Hangfire first opens a connection, from the final configuration
        services.AddHangfire((provider, configuration) => configuration
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseSqlServerStorage(
                () => new SqlConnection(provider.GetRequiredService<IConfiguration>().GetConnectionString("Database")),
                new SqlServerStorageOptions { PrepareSchemaIfNecessary = true }));
        if (runServer)
        {
            services.AddHangfireServer();
        }

        return services;
    }

    /// <summary>Adds or updates the recurring jobs. Their schedules are cron expressions from <c>Jobs:*</c>.</summary>
    public static void ScheduleJobs(this IServiceProvider services, IConfiguration configuration)
    {
        var recurring = services.GetRequiredService<IRecurringJobManager>();
        recurring.AddOrUpdate<TenantJobRunner>(
            "lock-due-groups",
            runner => runner.EnqueueForEveryTenantAsync(nameof(LockDueGroupsJob), CancellationToken.None),
            configuration["Jobs:LockDueGroups"] ?? throw new InvalidOperationException("Jobs:LockDueGroups is not set."));
    }
}
