using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Application.Abstractions;
using Domain.Grouping;

namespace Application.Grouping.LockDueGroups;

/// <summary>
/// Locks the tenant's open delivery groups whose deadline has passed (the end of Day 2), so they go out on Day 3
/// and the customer's next order opens a new group. Runs every few minutes; a group is locked as of its deadline
/// however late the job reaches it. Each group is saved on its own: one that an order or Ship now locked in the
/// meantime is skipped without holding up the rest.
/// </summary>
public class LockDueGroupsJob(IAppDbContext db, TimeProvider time, ILogger<LockDueGroupsJob> logger) : ITenantJob
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var due = await db.DeliveryGroups
            .Where(g => g.Status == DeliveryGroupStatus.Open && g.LocksAt <= now)
            .OrderBy(g => g.LocksAt)
            .ToListAsync(cancellationToken);

        var locked = 0;
        foreach (var group in due)
        {
            group.LockIfDue(now);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                locked++;
            }
            catch (DbUpdateConcurrencyException)
            {
                // Changed since it was read (an order past the deadline locked it first): it is no longer open
                db.Entry(group).State = EntityState.Detached;
            }
        }

        logger.LogInformation("Locked {Locked} of {Due} delivery groups past their deadline", locked, due.Count);
    }
}
