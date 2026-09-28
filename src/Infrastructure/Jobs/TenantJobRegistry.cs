using Application.Abstractions;

namespace Infrastructure.Jobs;

/// <summary>
/// The tenant jobs Hangfire may run, by class name. A queued job stores the name and the tenant id, never a .NET
/// type or a generic method (Hangfire cannot load a generic method back from storage), and the dashboard shows the
/// name. Only a registered name runs.
/// </summary>
public sealed class TenantJobRegistry
{
    private readonly Dictionary<string, Type> jobs;

    public TenantJobRegistry(params Type[] types)
    {
        var notJobs = types.Where(type => !typeof(ITenantJob).IsAssignableFrom(type)).Select(type => type.Name).ToList();
        if (notJobs.Count > 0)
        {
            throw new ArgumentException($"Not tenant jobs: {string.Join(", ", notJobs)}.", nameof(types));
        }

        jobs = types.ToDictionary(type => type.Name);
    }

    public Type Find(string name)
    {
        return jobs.TryGetValue(name, out var type)
            ? type
            : throw new InvalidOperationException($"No tenant job named {name} is registered.");
    }
}
