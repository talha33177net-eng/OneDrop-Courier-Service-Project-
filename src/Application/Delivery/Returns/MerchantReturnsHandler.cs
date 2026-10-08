using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Domain.Common;
using Domain.Delivery;
using Domain.Parcels;

namespace Application.Delivery.Returns;

/// <summary>A parcel on its way back to the merchant, why, and where it is now in words.</summary>
public sealed record ComingBack(string TrackingCode, string RecipientName, string Area, string? Reason, string Where);

/// <summary>The merchant's returns: what is coming back, and the return lists riders brought or are bringing.</summary>
public sealed record MerchantReturns(IReadOnlyList<ComingBack> ComingBack, IReadOnlyList<ReturnListRow> Lists)
{
    /// <summary>Lists a rider handed over that the merchant has still to confirm.</summary>
    public int ToConfirm => Lists.Count(list => list.Status == ReturnListStatus.HandedOver);
}

/// <summary>
/// The merchant's side of its returns: the parcels coming back and where each is, the return lists, and the
/// confirmation that a list's parcels arrived, its signature for them. A merchant sees and confirms only its own (the
/// merchant filter): another merchant's list is not found, as an unknown number is.
/// </summary>
public class MerchantReturnsHandler(IAppDbContext db, ICurrentUser currentUser, ReturnListsHandler lists, TimeProvider time)
{
    /// <summary>The most return lists the page shows, newest first.</summary>
    public const int MaxLists = 50;

    public async Task<MerchantReturns> GetAsync(CancellationToken cancellationToken = default)
    {
        var coming = await (
            from parcel in db.Parcels
            join area in db.Areas on parcel.AreaId equals area.Id
            where parcel.Status == ParcelStatus.Returning
            orderby parcel.Id
            select new
            {
                parcel.TrackingCode,
                parcel.RecipientName,
                Area = area.Name,
                parcel.ReturnReason,
                Here = db.Hubs.Where(h => h.Id == parcel.CurrentHubId).Select(h => h.Name).FirstOrDefault(),
                Bound = db.Hubs.Where(h => h.Id == parcel.TransferToHubId).Select(h => h.Name).FirstOrDefault(),
                OnList = (
                    from line in db.ReturnListParcels
                    join list in db.ReturnLists on line.ReturnListId equals list.Id
                    where line.ParcelId == parcel.Id && list.Status == ReturnListStatus.Out
                    select list.Number).FirstOrDefault()
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return new MerchantReturns(
            [
                .. coming.Select(p => new ComingBack(
                    p.TrackingCode,
                    p.RecipientName,
                    p.Area,
                    p.ReturnReason,
                    p.OnList is { } number ? $"On its way to you with a rider, on {number}"
                        : p.Here is { } hub ? $"At {hub}"
                        : p.Bound is { } next ? $"On the way to {next}"
                        : "With the rider, coming back to the hub"))
            ],
            await lists.RowsAsync(db.ReturnLists.OrderByDescending(l => l.Id).Take(MaxLists), cancellationToken));
    }

    /// <summary>One of the merchant's return lists with its parcels.</summary>
    public async Task<ReturnListView?> ListAsync(string number, CancellationToken cancellationToken = default)
    {
        var code = number.Trim().ToUpperInvariant();
        var row = (await lists.RowsAsync(db.ReturnLists.Where(l => l.Number == code), cancellationToken)).SingleOrDefault();

        return row is null ? null : new ReturnListView(row, (await lists.ParcelsAsync([code], cancellationToken))[code]);
    }

    /// <summary>The merchant confirms it received the parcels of a list the rider handed over; a note says what was not right.</summary>
    public async Task<Result> ConfirmAsync(string number, string? note, CancellationToken cancellationToken = default)
    {
        var code = number.Trim().ToUpperInvariant();
        var list = await db.ReturnLists.SingleOrDefaultAsync(l => l.Number == code, cancellationToken);
        if (list is null)
        {
            return ReturnListsHandler.NotFound;
        }

        var confirmed = list.Confirm(currentUser.UserId, note, time.GetUtcNow().UtcDateTime);
        if (confirmed.IsFailure)
        {
            return confirmed;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict("returnList.changed", "This return list changed just now. Open it again.");
        }

        return Result.Success();
    }
}
