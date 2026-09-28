using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Domain.Delivery;
using Domain.Orders;

namespace Application.Delivery;

/// <summary>
/// The parcels of one delivery still for delivery (not cancelled, refused or returned): what they put on a bike, how
/// many are at the delivery's hub now, and what a rider has taken out (orders out for delivery or delivered).
/// </summary>
public sealed record DeliveryParcels(TripLoad Load, int AtHub, TripLoad Taken)
{
    public static DeliveryParcels None => new(TripLoad.None, 0, TripLoad.None);
}

internal static class DeliveryParcelsQuery
{
    /// <summary>The parcels of each of <paramref name="groupIds"/>; a delivery with none is missing from the answer.</summary>
    public static async Task<Dictionary<long, DeliveryParcels>> ParcelsAsync(
        this IAppDbContext db,
        IReadOnlyCollection<long> groupIds,
        CancellationToken cancellationToken)
    {
        var rows = await (
            from package in db.Packages
            join order in db.Orders on package.OrderId equals order.Id
            join g in db.DeliveryGroups on order.DeliveryGroupId equals g.Id
            where groupIds.Contains(order.DeliveryGroupId)
            select new { order.DeliveryGroupId, order.Status, package.WeightGrams, AtHub = package.HubId == g.HubId })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return rows
            .Where(row => Order.IsForDelivery(row.Status))
            .GroupBy(row => row.DeliveryGroupId)
            .ToDictionary(
                parcels => parcels.Key,
                parcels => new DeliveryParcels(
                    new TripLoad(parcels.Count(), parcels.Sum(row => row.WeightGrams)),
                    parcels.Count(row => row.AtHub),
                    parcels
                        .Where(row => row.Status is OrderStatus.OutForDelivery or OrderStatus.Delivered)
                        .Aggregate(TripLoad.None, (load, row) => load + new TripLoad(1, row.WeightGrams))));
    }

    public static DateOnly LocalDay(this TimeZoneInfo timeZone, DateTime utc)
    {
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, timeZone));
    }
}
