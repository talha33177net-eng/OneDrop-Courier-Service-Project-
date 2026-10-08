namespace Application.Abstractions;

/// <summary>When a recurring job last ran and runs next, as UTC; both null when the scheduler cannot say.</summary>
public sealed record ScheduledTimes(DateTime? LastRun, DateTime? NextRun);

/// <summary>Reads the background scheduler, so a page can show whether a recurring job is running at all.</summary>
public interface IJobSchedule
{
    Task<ScheduledTimes> TimesAsync(string recurringJobId, CancellationToken cancellationToken = default);
}
