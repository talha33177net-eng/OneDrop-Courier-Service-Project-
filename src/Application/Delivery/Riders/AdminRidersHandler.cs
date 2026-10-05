using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Application.Common;
using Domain.Common;
using Domain.Delivery;

namespace Application.Delivery.Riders;

public sealed record RiderRow(
    long Id,
    string Name,
    string Phone,
    long HubId,
    string Hub,
    Vehicle Vehicle,
    string? Email,
    bool Active,
    int WithThem,
    int DeliveredToday,
    decimal CashInHand);

public sealed record NewRider(string? Name, string? Phone, long HubId, Vehicle Vehicle, string? Email, string? Password);

/// <summary>
/// The courier's riders: list them by hub with today's work, add one with a login, change their hub or details, or stop
/// and restart them. Courier admins only.
/// </summary>
public class AdminRidersHandler(IAppDbContext db, ITenantContext tenantContext, IUserAccounts accounts, TimeProvider time)
{
    public static readonly Error NotFound = Error.NotFound("rider.notFound", "That rider was not found.");

    public async Task<IReadOnlyList<RiderRow>> ListAsync(long? hubId, CancellationToken cancellationToken = default)
    {
        var tenant = tenantContext.Require();
        var todayStart = tenant.StartUtc(tenant.Today(time.GetUtcNow().UtcDateTime));
        var riders = db.Riders.AsQueryable();
        if (hubId is { } only)
        {
            riders = riders.Where(r => r.HubId == only);
        }

        var rows = await (
            from rider in riders
            join hub in db.Hubs on rider.HubId equals hub.Id
            orderby rider.Archived, hub.Name, rider.Name
            select new
            {
                rider.UserId,
                Row = new RiderRow(
                    rider.Id,
                    rider.Name,
                    rider.Phone,
                    hub.Id,
                    hub.Name,
                    rider.Vehicle,
                    null,
                    !rider.Archived,
                    db.DeliveryAttempts.Count(a => a.RiderId == rider.Id && a.Outcome == null),
                    db.DeliveryAttempts.Count(a => a.RiderId == rider.Id && a.CompletedOn >= todayStart &&
                        (a.Outcome == AttemptOutcome.Delivered || a.Outcome == AttemptOutcome.PartlyDelivered)),
                    (from a in db.DeliveryAttempts
                        join run in db.DeliveryRuns on a.RunId equals run.Id
                        where run.RiderId == rider.Id && run.Status == RunStatus.Open
                        select (decimal?)a.CollectedAmount).Sum() ?? 0)
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var emails = await accounts.EmailsAsync([.. rows.Select(r => r.UserId).OfType<long>()], cancellationToken);

        return [.. rows.Select(r => r.Row with { Email = r.UserId is { } id ? emails.GetValueOrDefault(id) : null })];
    }

    public async Task<Result> AddAsync(NewRider spec, CancellationToken cancellationToken = default)
    {
        if (!await db.Hubs.AnyAsync(h => h.Id == spec.HubId && !h.Archived, cancellationToken))
        {
            return Error.Validation("rider.hub", "Choose the hub the rider works from.");
        }

        var rider = Rider.Create(spec.HubId, spec.Name, spec.Phone, spec.Vehicle, null);
        if (rider.IsFailure)
        {
            return rider.Error!;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var login = await accounts.CreateAsync(new NewLogin(spec.Email, spec.Password, rider.Value.Name, Roles.Rider), cancellationToken);
        if (login.IsFailure)
        {
            return login.Error!;
        }

        rider.Value.LinkLogin(login.Value);
        db.Riders.Add(rider.Value);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<Result> EditAsync(
        long id,
        string? name,
        string? phone,
        long hubId,
        Vehicle vehicle,
        CancellationToken cancellationToken = default)
    {
        var rider = await db.Riders.SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (rider is null)
        {
            return NotFound;
        }

        if (!await db.Hubs.AnyAsync(h => h.Id == hubId && !h.Archived, cancellationToken))
        {
            return Error.Validation("rider.hub", "Choose the hub the rider works from.");
        }

        if (hubId != rider.HubId && await BusyAsync(rider, "moved to another hub", cancellationToken) is { } busy)
        {
            return busy;
        }

        var changed = rider.Change(hubId, name, phone, vehicle);
        if (changed.IsSuccess)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return changed;
    }

    public async Task<Result> SetActiveAsync(long id, bool active, CancellationToken cancellationToken = default)
    {
        var rider = await db.Riders.SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (rider is null)
        {
            return NotFound;
        }

        if (active)
        {
            rider.Reactivate();
        }
        else
        {
            if (await BusyAsync(rider, "stopped", cancellationToken) is { } busy)
            {
                return busy;
            }

            rider.Deactivate();
        }

        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    /// <summary>
    /// Why the rider cannot leave their hub yet: a stopped rider cannot sign in and a moved one hands their cash in at the
    /// wrong hub, so parcels with them, an open run or a pickup they were sent on would be stuck.
    /// </summary>
    private async Task<Error?> BusyAsync(Rider rider, string what, CancellationToken cancellationToken)
    {
        var parcels = await db.Parcels.CountAsync(p => p.RiderId == rider.Id, cancellationToken);
        var runs = await db.DeliveryRuns.CountAsync(r => r.RiderId == rider.Id && r.Status == RunStatus.Open, cancellationToken);
        var pickups = await db.PickupRequests.CountAsync(r => r.RiderId == rider.Id && r.Status == PickupStatus.Assigned, cancellationToken);
        if (parcels + runs + pickups == 0)
        {
            return null;
        }

        List<string> open = [];
        if (parcels > 0)
        {
            open.Add($"{parcels} parcel{(parcels == 1 ? "" : "s")} with them");
        }

        if (runs > 0)
        {
            open.Add($"{runs} run{(runs == 1 ? "" : "s")} not closed");
        }

        if (pickups > 0)
        {
            open.Add($"{pickups} pickup{(pickups == 1 ? "" : "s")} to make");
        }

        return Error.Conflict(
            "rider.busy",
            $"{rider.Name} cannot be {what} yet: {string.Join(", ", open)}. Close their run at the hub and give their " +
            "pickups to another rider first.");
    }
}
