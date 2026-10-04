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
    string? Email,
    bool Active,
    int WithThem,
    int DeliveredToday,
    decimal CashInHand);

public sealed record NewRider(string? Name, string? Phone, long HubId, string? Email, string? Password);

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

        var rider = Rider.Create(spec.HubId, spec.Name, spec.Phone, null);
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

    public async Task<Result> EditAsync(long id, string? name, string? phone, long hubId, CancellationToken cancellationToken = default)
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

        var changed = rider.Change(hubId, name, phone);
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
            rider.Deactivate();
        }

        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
