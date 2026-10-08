using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Application.Common;
using Application.Parcels.Browse;
using Domain.Common;
using Domain.Parcels;

namespace Application.Parcels.Requests;

/// <summary>A request as the lists show it, with the parcel it is about. Times are the tenant's.</summary>
public sealed record RequestRow(
    string TrackingCode,
    string Merchant,
    ParcelStatus ParcelStatus,
    ParcelRequestKind Kind,
    decimal CodAmount,
    decimal? NewCodAmount,
    string Reason,
    ParcelRequestStatus Status,
    string? Answer,
    DateTime Asked,
    DateTime? AnsweredOn);

/// <summary>
/// Requests about parcels on their way: a merchant asks to cancel one (it comes back as a return) or to change its
/// cash on delivery, and the courier's admins approve, which changes the parcel there and then, or refuse with a
/// reason. A request is found by its parcel's tracking code, since a parcel has one open request at a time. A merchant
/// sees and asks only about its own parcels (the merchant filter).
/// </summary>
public class ParcelRequestsHandler(IAppDbContext db, ITenantContext tenantContext, ICurrentUser currentUser, TimeProvider time)
{
    /// <summary>The most requests a list shows, newest first.</summary>
    public const int MaxRows = 100;

    /// <summary>How long the courier keeps seeing the requests it has answered.</summary>
    public const int RecentDays = 14;

    public static readonly Error NoOpenRequest = Error.NotFound("request.notFound", "There is no open request about that parcel.");

    public async Task<Result> AskAsync(
        string trackingCode,
        ParcelRequestKind kind,
        decimal? newCodAmount,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var code = trackingCode.Trim().ToUpperInvariant();
        var parcel = await db.Parcels.SingleOrDefaultAsync(p => p.TrackingCode == code, cancellationToken);
        if (parcel is null)
        {
            return ParcelDetailsHandler.NotFound;
        }

        if (await db.ParcelRequests.AnyAsync(r => r.ParcelId == parcel.Id && r.Status == ParcelRequestStatus.Open, cancellationToken))
        {
            return Error.Conflict("request.open", $"You already asked about {code}. Wait for the courier's answer.");
        }

        var asked = ParcelRequest.Ask(parcel, kind, newCodAmount, reason, currentUser.UserId);
        if (asked.IsFailure)
        {
            return asked.Error!;
        }

        db.ParcelRequests.Add(asked.Value);
        try
        {
            return await SaveAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two requests about one parcel at the same moment: the second meets the one-open-request index
            return Error.Conflict("request.open", $"Someone asked about {code} just now. Wait for the courier's answer.");
        }
    }

    /// <summary>The requests about one parcel, newest first.</summary>
    public Task<IReadOnlyList<RequestRow>> ForParcelAsync(string trackingCode, CancellationToken cancellationToken = default)
    {
        var code = trackingCode.Trim().ToUpperInvariant();

        return RowsAsync(db.ParcelRequests.Where(r => db.Parcels.Any(p => p.Id == r.ParcelId && p.TrackingCode == code)), cancellationToken);
    }

    /// <summary>Every request of the merchant signed in, newest first.</summary>
    public Task<IReadOnlyList<RequestRow>> MerchantAsync(CancellationToken cancellationToken = default)
    {
        return RowsAsync(db.ParcelRequests, cancellationToken);
    }

    /// <summary>The courier's open requests, and those it answered lately.</summary>
    public async Task<(IReadOnlyList<RequestRow> Open, IReadOnlyList<RequestRow> Answered)> CourierAsync(CancellationToken cancellationToken = default)
    {
        var since = time.GetUtcNow().UtcDateTime.AddDays(-RecentDays);

        return (
            await RowsAsync(db.ParcelRequests.Where(r => r.Status == ParcelRequestStatus.Open), cancellationToken),
            await RowsAsync(db.ParcelRequests.Where(r => r.Status != ParcelRequestStatus.Open && r.AnsweredOn >= since), cancellationToken));
    }

    /// <summary>The courier agrees to the open request about <paramref name="trackingCode"/>, and the parcel changes.</summary>
    public Task<Result> ApproveAsync(string trackingCode, string? answer, CancellationToken cancellationToken = default)
    {
        return AnswerAsync(trackingCode, (request, parcel, now) => request.Approve(parcel, answer, currentUser.UserId, now), cancellationToken);
    }

    /// <summary>The courier says no to the open request about <paramref name="trackingCode"/>, and why.</summary>
    public Task<Result> RefuseAsync(string trackingCode, string? answer, CancellationToken cancellationToken = default)
    {
        return AnswerAsync(trackingCode, (request, _, now) => request.Refuse(answer, currentUser.UserId, now), cancellationToken);
    }

    private async Task<Result> AnswerAsync(
        string trackingCode,
        Func<ParcelRequest, Parcel, DateTime, Result> answer,
        CancellationToken cancellationToken)
    {
        var code = trackingCode.Trim().ToUpperInvariant();
        var parcel = await db.Parcels.SingleOrDefaultAsync(p => p.TrackingCode == code, cancellationToken);
        var request = parcel is null
            ? null
            : await db.ParcelRequests.SingleOrDefaultAsync(r => r.ParcelId == parcel.Id && r.Status == ParcelRequestStatus.Open, cancellationToken);
        if (request is null)
        {
            return NoOpenRequest;
        }

        var answered = answer(request, parcel!, time.GetUtcNow().UtcDateTime);

        return answered.IsFailure ? answered : await SaveAsync(cancellationToken);
    }

    /// <summary>The requests as rows, newest first and at most <see cref="MaxRows"/>; the projection comes last.</summary>
    private async Task<IReadOnlyList<RequestRow>> RowsAsync(IQueryable<ParcelRequest> requests, CancellationToken cancellationToken)
    {
        var tenant = tenantContext.Require();
        var rows = await (
            from request in requests
            join parcel in db.Parcels on request.ParcelId equals parcel.Id
            join merchant in db.Merchants on request.MerchantId equals merchant.Id
            orderby request.Id descending
            select new RequestRow(
                parcel.TrackingCode,
                merchant.Name,
                parcel.Status,
                request.Kind,
                request.CodAmount,
                request.NewCodAmount,
                request.Reason,
                request.Status,
                request.Answer,
                request.Created,
                request.AnsweredOn))
            .Take(MaxRows)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return
        [
            .. rows.Select(row => row with
            {
                Asked = tenant.Local(row.Asked),
                AnsweredOn = row.AnsweredOn is { } answered ? tenant.Local(answered) : null
            })
        ];
    }

    private async Task<Result> SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict("request.changed", "The parcel changed just now. Open it again.");
        }

        return Result.Success();
    }
}
