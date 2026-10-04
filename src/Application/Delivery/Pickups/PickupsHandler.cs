using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Application.Common;
using Application.Hubs;
using Domain.Common;
using Domain.Delivery;
using Domain.Parcels;

namespace Application.Delivery.Pickups;

/// <summary>A merchant's pickup point and how many of its parcels wait there.</summary>
public sealed record PickupPointRow(long Id, string Name, string Address, string Area, bool IsDefault, int Waiting);

/// <summary>A pickup request as the merchant's and the hub's pages show it.</summary>
public sealed record PickupRow(
    long Id,
    string Merchant,
    string MerchantPhone,
    string PickupPoint,
    string Address,
    string Area,
    string ContactPhone,
    DateOnly Date,
    int Expected,
    int Waiting,
    string? Note,
    PickupStatus Status,
    long? RiderId,
    string? Rider,
    string? RiderPhone,
    int? Picked);

public sealed record MerchantPickups(IReadOnlyList<PickupPointRow> Points, IReadOnlyList<PickupRow> Requests);

/// <summary>
/// Pickup requests: a merchant asks for a rider to collect at one of its pickup points, and the hub of that point's zone
/// sends one. A merchant sees only its own requests; a hub sees the requests it collects.
/// </summary>
public class PickupsHandler(
    IAppDbContext db,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    HubDirectory hubs,
    TimeProvider time)
{
    public static readonly Error NotFound = Error.NotFound("pickup.notFound", "That pickup request was not found.");

    public async Task<MerchantPickups> ForMerchantAsync(CancellationToken cancellationToken = default)
    {
        var points = await (
            from point in db.PickupPoints
            join area in db.Areas on point.AreaId equals area.Id
            where !point.Archived
            orderby point.IsDefault descending, point.Name
            select new PickupPointRow(
                point.Id,
                point.Name,
                point.Address,
                area.Name,
                point.IsDefault,
                db.Parcels.Count(p => p.PickupPointId == point.Id && p.Status == ParcelStatus.Pending)))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return new MerchantPickups(points, await RowsAsync(db.PickupRequests.OrderByDescending(r => r.Id).Take(30), cancellationToken));
    }

    public async Task<Result> RequestAsync(
        long pickupPointId,
        DateOnly date,
        int expected,
        string? note,
        CancellationToken cancellationToken = default)
    {
        var tenant = tenantContext.Require();
        var merchantId = currentUser.MerchantId ?? throw new InvalidOperationException("A merchant asks for pickups.");
        var merchant = await db.Merchants.SingleAsync(m => m.Id == merchantId, cancellationToken);
        if (!merchant.CanBook)
        {
            return Parcels.CreateParcel.CreateParcelHandler.NotActive;
        }

        var point = await (
            from p in db.PickupPoints
            join area in db.Areas on p.AreaId equals area.Id
            join zone in db.Zones on area.ZoneId equals zone.Id
            where p.Id == pickupPointId && !p.Archived
            select new { p.Id, zone.HubId })
            .SingleOrDefaultAsync(cancellationToken);
        if (point is null)
        {
            return Error.Validation("pickup.point", "Choose one of your pickup points.");
        }

        var already = await db.PickupRequests.AnyAsync(
            r => r.PickupPointId == point.Id && r.PickupDate == date &&
                (r.Status == PickupStatus.Requested || r.Status == PickupStatus.Assigned),
            cancellationToken);
        if (already)
        {
            return Error.Conflict("pickup.duplicate", "A rider is already coming to this pickup point that day.");
        }

        var created = PickupRequest.Create(
            merchantId,
            point.Id,
            point.HubId,
            date,
            tenant.Today(time.GetUtcNow().UtcDateTime),
            expected,
            note);
        if (created.IsFailure)
        {
            return created.Error!;
        }

        db.PickupRequests.Add(created.Value);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<Result> CancelAsync(long id, CancellationToken cancellationToken = default)
    {
        var request = await db.PickupRequests.SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (request is null)
        {
            return NotFound;
        }

        var cancelled = request.Cancel();
        if (cancelled.IsSuccess)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return cancelled;
    }

    /// <summary>The hub's open pickups (requested or assigned, any day) and the ones completed today.</summary>
    public async Task<IReadOnlyList<PickupRow>?> ForHubAsync(string hubCode, CancellationToken cancellationToken = default)
    {
        var tenant = tenantContext.Require();
        var hub = await hubs.FindAsync(hubCode, cancellationToken);
        if (hub is null)
        {
            return null;
        }

        var todayStart = tenant.StartUtc(tenant.Today(time.GetUtcNow().UtcDateTime));
        var requests = db.PickupRequests
            .Where(r => r.HubId == hub.Id &&
                (r.Status == PickupStatus.Requested || r.Status == PickupStatus.Assigned ||
                    (r.Status == PickupStatus.Completed && r.CompletedOn >= todayStart)))
            .OrderBy(r => r.Status)
            .ThenBy(r => r.PickupDate)
            .ThenBy(r => r.Id);

        return await RowsAsync(requests, cancellationToken);
    }

    public async Task<Result> AssignAsync(string hubCode, long requestId, long riderId, CancellationToken cancellationToken = default)
    {
        var hub = await hubs.FindAsync(hubCode, cancellationToken);
        var request = hub is null
            ? null
            : await db.PickupRequests.SingleOrDefaultAsync(r => r.Id == requestId && r.HubId == hub.Id, cancellationToken);
        if (request is null)
        {
            return NotFound;
        }

        var rider = await db.Riders.SingleOrDefaultAsync(r => r.Id == riderId, cancellationToken);
        if (rider is null)
        {
            return Hubs.AssignParcels.AssignParcelsHandler.UnknownRider;
        }

        var assigned = request.Assign(rider);
        if (assigned.IsSuccess)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return assigned;
    }

    private async Task<IReadOnlyList<PickupRow>> RowsAsync(IQueryable<PickupRequest> requests, CancellationToken cancellationToken)
    {
        return await (
            from request in requests
            join merchant in db.Merchants on request.MerchantId equals merchant.Id
            join point in db.PickupPoints on request.PickupPointId equals point.Id
            join area in db.Areas on point.AreaId equals area.Id
            select new PickupRow(
                request.Id,
                merchant.Name,
                merchant.ContactPhone,
                point.Name,
                point.Address,
                area.Name,
                point.ContactPhone,
                request.PickupDate,
                request.ExpectedParcels,
                db.Parcels.Count(p => p.PickupPointId == point.Id && p.Status == ParcelStatus.Pending),
                request.Note,
                request.Status,
                request.RiderId,
                db.Riders.Where(r => r.Id == request.RiderId).Select(r => r.Name).FirstOrDefault(),
                db.Riders.Where(r => r.Id == request.RiderId).Select(r => r.Phone).FirstOrDefault(),
                request.PickedParcels))
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }
}
