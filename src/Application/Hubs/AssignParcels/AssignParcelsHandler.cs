using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Application.Common;
using Domain.Common;
using Domain.Delivery;
using Domain.Network;

namespace Application.Hubs.AssignParcels;

/// <summary>A rider of the hub and what they carry today.</summary>
public sealed record RiderLoad(long Id, string Name, string Phone, int WithThem, int DeliveredToday);

/// <summary>What an assignment did: the parcels handed over, and the ones that could not be with why.</summary>
public sealed record AssignResult(int Assigned, IReadOnlyList<string> Problems);

/// <summary>
/// Hub staff hand parcels waiting at their hub to a rider for delivery. The parcels go on the rider's run sheet for the
/// day, opened with the first parcel; each becomes out for delivery. A parcel that cannot go (not here, delivered from
/// another hub) is listed with the reason and the others still go.
/// </summary>
public class AssignParcelsHandler(IAppDbContext db, ITenantContext tenantContext, HubDirectory hubs, TimeProvider time)
{
    public static readonly Error UnknownRider = Error.Validation("assign.rider", "Choose an active rider of this hub.");

    public async Task<IReadOnlyList<RiderLoad>> RidersAsync(string hubCode, CancellationToken cancellationToken = default)
    {
        var tenant = tenantContext.Require();
        var hub = await hubs.FindAsync(hubCode, cancellationToken);
        if (hub is null)
        {
            return [];
        }

        var today = tenant.Today(time.GetUtcNow().UtcDateTime);
        var todayStart = tenant.StartUtc(today);

        return await db.Riders
            .Where(r => r.HubId == hub.Id && !r.Archived)
            .OrderBy(r => r.Name)
            .Select(r => new RiderLoad(
                r.Id,
                r.Name,
                r.Phone,
                db.DeliveryAttempts.Count(a => a.RiderId == r.Id && a.Outcome == null),
                db.DeliveryAttempts.Count(a => a.RiderId == r.Id && a.CompletedOn >= todayStart &&
                    (a.Outcome == AttemptOutcome.Delivered || a.Outcome == AttemptOutcome.PartlyDelivered))))
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<Result<AssignResult>> AssignAsync(
        string hubCode,
        long riderId,
        IReadOnlyCollection<string> trackingCodes,
        CancellationToken cancellationToken = default)
    {
        var tenant = tenantContext.Require();
        var hub = await hubs.FindAsync(hubCode, cancellationToken);
        if (hub is null)
        {
            return Hubs.HubScan.HubScanHandler.UnknownHub;
        }

        var rider = await db.Riders.SingleOrDefaultAsync(r => r.Id == riderId && r.HubId == hub.Id && !r.Archived, cancellationToken);
        if (rider is null)
        {
            return UnknownRider;
        }

        var codes = trackingCodes.Select(c => c.Trim().ToUpperInvariant()).Where(c => c.Length > 0).Distinct().ToList();
        if (codes.Count == 0)
        {
            return Error.Validation("assign.none", "Choose at least one parcel to hand to the rider.");
        }

        var now = time.GetUtcNow().UtcDateTime;
        var run = await RunAsync(rider, hub, tenant.Today(now), cancellationToken);
        if (run.Status != RunStatus.Open)
        {
            return Error.Conflict("assign.runClosed", $"{rider.Name}'s run for today is already closed.");
        }

        var parcels = await db.Parcels.Where(p => codes.Contains(p.TrackingCode)).ToListAsync(cancellationToken);
        var problems = codes.Except(parcels.Select(p => p.TrackingCode)).Select(code => $"{code} was not found.").ToList();
        var assigned = 0;
        foreach (var parcel in parcels)
        {
            var handed = parcel.AssignTo(rider.Id, hub.Id);
            if (handed.IsFailure)
            {
                problems.Add(handed.Error!.Message);
                continue;
            }

            db.DeliveryAttempts.Add(run.Add(parcel, now).Value);
            assigned++;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Error.Conflict("assign.changed", "Some of these parcels were moved by someone else just now. Reload and try again.");
        }

        return new AssignResult(assigned, problems);
    }

    /// <summary>The rider's run sheet for the day, opened (and saved, for its id) with the first parcel.</summary>
    private async Task<DeliveryRun> RunAsync(Rider rider, Hub hub, DateOnly today, CancellationToken cancellationToken)
    {
        var run = await db.DeliveryRuns.SingleOrDefaultAsync(r => r.RiderId == rider.Id && r.RunDate == today, cancellationToken);
        if (run is not null)
        {
            return run;
        }

        run = DeliveryRun.Open(rider, today);
        db.DeliveryRuns.Add(run);
        try
        {
            await db.SaveChangesAsync(cancellationToken);

            return run;
        }
        catch (DbUpdateException)
        {
            // Another assignment opened the run at the same moment: use that one
            db.Entry(run).State = EntityState.Detached;

            return await db.DeliveryRuns.SingleAsync(r => r.RiderId == rider.Id && r.RunDate == today, cancellationToken);
        }
    }
}
