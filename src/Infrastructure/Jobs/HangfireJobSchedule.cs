using Hangfire;
using Hangfire.Storage;
using Microsoft.Extensions.Logging;
using Application.Abstractions;

namespace Infrastructure.Jobs;

/// <summary>
/// Reads a recurring job's last and next run from Hangfire's storage. Hangfire keeps one schedule for every courier,
/// so the times are the same for each. An unreachable store answers "cannot say" rather than breaking the page.
/// </summary>
public class HangfireJobSchedule(JobStorage storage, ILogger<HangfireJobSchedule> logger) : IJobSchedule
{
    public Task<ScheduledTimes> TimesAsync(string recurringJobId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = storage.GetConnection();
            var job = connection.GetRecurringJobs([recurringJobId]).SingleOrDefault();

            return Task.FromResult(new ScheduledTimes(job?.LastExecution, job?.NextExecution));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not read the schedule of {Job}", recurringJobId);

            return Task.FromResult(new ScheduledTimes(null, null));
        }
    }
}
