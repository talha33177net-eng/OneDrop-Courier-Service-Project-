using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Application.Common;
using Domain.Common;
using Domain.Delivery;

namespace Application.Hubs.Runs;

/// <summary>A rider's run as the hub's closing page shows it.</summary>
public sealed record RunRow(
    long Id,
    string Rider,
    string RiderPhone,
    DateOnly Date,
    RunStatus Status,
    int Assigned,
    int Delivered,
    int Held,
    int Refused,
    int WithoutOutcome,
    decimal CashCollected,
    decimal? CashReceived,
    DateTime? ClosedOn)
{
    public decimal? Short => CashReceived is null ? null : CashCollected - CashReceived;

    /// <summary>When the rider is due back with the cash, on the courier's clock; null when it sets no time.</summary>
    public DateTime? DueBack { get; init; }

    /// <summary>Still open after the rider was due back.</summary>
    public bool Late { get; init; }
}

/// <summary>
/// The end of a rider's day at the hub: their run sheets still open (today's and any earlier ones) and today's closed
/// ones. Closing counts the cash the deliveries collected against what the rider handed in and takes back the parcels
/// they could not deliver, which are then at the hub again.
/// </summary>
public class RunsHandler(IAppDbContext db, ITenantContext tenantContext, HubDirectory hubs, TimeProvider time)
{
    public static readonly Error NotFound = Error.NotFound("run.notFound", "That run was not found at this hub.");

    public async Task<IReadOnlyList<RunRow>?> ListAsync(string hubCode, CancellationToken cancellationToken = default)
    {
        var tenant = tenantContext.Require();
        var hub = await hubs.FindAsync(hubCode, cancellationToken);
        if (hub is null)
        {
            return null;
        }

        var now = time.GetUtcNow().UtcDateTime;
        var today = tenant.Today(now);
        var rows = await (
            from run in db.DeliveryRuns
            join rider in db.Riders on run.RiderId equals rider.Id
            where run.HubId == hub.Id && (run.Status == RunStatus.Open || run.RunDate == today)
            orderby run.Status, run.RunDate, rider.Name
            select new RunRow(
                run.Id,
                rider.Name,
                rider.Phone,
                run.RunDate,
                run.Status,
                db.DeliveryAttempts.Count(a => a.RunId == run.Id),
                db.DeliveryAttempts.Count(a => a.RunId == run.Id &&
                    (a.Outcome == AttemptOutcome.Delivered || a.Outcome == AttemptOutcome.PartlyDelivered)),
                db.DeliveryAttempts.Count(a => a.RunId == run.Id && a.Outcome == AttemptOutcome.Hold),
                db.DeliveryAttempts.Count(a => a.RunId == run.Id && a.Outcome == AttemptOutcome.Refused),
                db.DeliveryAttempts.Count(a => a.RunId == run.Id && a.Outcome == null),
                db.DeliveryAttempts.Where(a => a.RunId == run.Id).Sum(a => (decimal?)a.CollectedAmount) ?? 0,
                run.CashReceived,
                run.ClosedOn))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return
        [
            .. rows.Select(row => row with
            {
                ClosedOn = row.ClosedOn is { } closed ? tenant.Local(closed) : null,
                DueBack = tenant.DueBack(row.Date),
                Late = row.Status == RunStatus.Open && tenant.RunIsLate(row.Date, now)
            })
        ];
    }

    /// <summary>
    /// The rider handed in <paramref name="received"/> in cash. Every parcel of the run needs an outcome first; the ones
    /// held or refused come back to the hub with the closing.
    /// </summary>
    public async Task<Result> CloseAsync(string hubCode, long runId, decimal received, CancellationToken cancellationToken = default)
    {
        var hub = await hubs.FindAsync(hubCode, cancellationToken);
        var run = hub is null
            ? null
            : await db.DeliveryRuns.SingleOrDefaultAsync(r => r.Id == runId && r.HubId == hub.Id, cancellationToken);
        if (run is null)
        {
            return NotFound;
        }

        var attempts = await db.DeliveryAttempts.Where(a => a.RunId == run.Id).ToListAsync(cancellationToken);
        var open = attempts.Count(a => !a.IsDone);
        if (open > 0)
        {
            return Error.Conflict(
                "run.open",
                $"The rider has not recorded what happened to {open} parcel{(open == 1 ? "" : "s")} yet. They record it on their phone first.");
        }

        var closed = run.Close(attempts.Sum(a => a.CollectedAmount), received, time.GetUtcNow().UtcDateTime);
        if (closed.IsFailure)
        {
            return closed;
        }

        // The parcels the rider brings back: held for another day or refused, still with this rider
        var backIds = attempts
            .Where(a => a.Outcome is AttemptOutcome.Hold or AttemptOutcome.Refused)
            .Select(a => a.ParcelId)
            .ToList();
        var back = await db.Parcels
            .Where(p => backIds.Contains(p.Id) && p.RiderId == run.RiderId)
            .ToListAsync(cancellationToken);
        foreach (var parcel in back)
        {
            parcel.ReceiveAt(run.HubId, run.RunDate);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict("run.changed", "This run changed just now. Reload and close it again.");
        }

        return Result.Success();
    }
}
