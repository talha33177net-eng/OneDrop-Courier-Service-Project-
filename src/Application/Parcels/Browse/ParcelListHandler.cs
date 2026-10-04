using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Application.Common;
using Domain.Common;
using Domain.Parcels;

namespace Application.Parcels.Browse;

/// <summary>The tabs a parcel list is cut into, each a group of statuses.</summary>
public enum ParcelTab
{
    All,
    Pending,
    InTransit,
    OutForDelivery,
    OnHold,
    Delivered,
    Returns,
    Cancelled
}

public sealed record ParcelQuery
{
    public ParcelTab Tab { get; init; }

    /// <summary>A tracking code, the merchant's reference, the recipient's name or phone.</summary>
    public string? Search { get; init; }

    /// <summary>Courier staff only: one merchant's parcels.</summary>
    public long? MerchantId { get; init; }

    /// <summary>Courier staff only: parcels collected or delivered by one hub.</summary>
    public long? HubId { get; init; }

    /// <summary>Booked on or after this day (the tenant's date).</summary>
    public DateOnly? From { get; init; }

    /// <summary>Booked on or before this day.</summary>
    public DateOnly? To { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 25;
}

/// <summary>A parcel as a list shows it. Times are the tenant's.</summary>
public sealed record ParcelRow(
    string TrackingCode,
    string? MerchantReference,
    string Merchant,
    string RecipientName,
    string RecipientPhone,
    string Area,
    string DeliveryHub,
    ParcelStatus Status,
    decimal CodAmount,
    decimal? CollectedAmount,
    decimal DeliveryCharge,
    int Attempts,
    DateTime Booked);

public sealed record ParcelList(Page<ParcelRow> Page, IReadOnlyDictionary<ParcelTab, int> Counts);

/// <summary>
/// Lists parcels with tabs, search, date range and paging. A merchant sees only its own (the merchant filter); courier
/// staff see every merchant's and can narrow to one merchant or hub.
/// </summary>
public class ParcelListHandler(IAppDbContext db, ITenantContext tenantContext)
{
    public const int MaxPageSize = 100;

    public static ParcelStatus[] StatusesOf(ParcelTab tab)
    {
        return tab switch
        {
            ParcelTab.Pending => [ParcelStatus.Pending],
            ParcelTab.InTransit => [ParcelStatus.PickedUp, ParcelStatus.AtHub, ParcelStatus.InTransit],
            ParcelTab.OutForDelivery => [ParcelStatus.OutForDelivery],
            ParcelTab.OnHold => [ParcelStatus.OnHold],
            ParcelTab.Delivered => [ParcelStatus.Delivered, ParcelStatus.PartlyDelivered],
            ParcelTab.Returns => [ParcelStatus.Returning, ParcelStatus.Returned],
            ParcelTab.Cancelled => [ParcelStatus.Cancelled],
            _ => Enum.GetValues<ParcelStatus>()
        };
    }

    public async Task<ParcelList> ListAsync(ParcelQuery query, CancellationToken cancellationToken = default)
    {
        var tenant = tenantContext.Require();
        var parcels = Filter(db.Parcels.AsNoTracking(), query, tenant);

        var byStatus = await parcels
            .GroupBy(p => p.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Status, g => g.Count, cancellationToken);
        var counts = Enum.GetValues<ParcelTab>()
            .ToDictionary(tab => tab, tab => StatusesOf(tab).Sum(status => byStatus.GetValueOrDefault(status)));

        var statuses = StatusesOf(query.Tab);
        var size = Math.Clamp(query.PageSize, 1, MaxPageSize);
        var number = Math.Max(1, query.Page);
        var inTab = parcels.Where(p => statuses.Contains(p.Status));
        var rows = await (
            from parcel in inTab
            join merchant in db.Merchants on parcel.MerchantId equals merchant.Id
            join area in db.Areas on parcel.AreaId equals area.Id
            join hub in db.Hubs on parcel.DeliveryHubId equals hub.Id
            orderby parcel.Id descending
            select new ParcelRow(
                parcel.TrackingCode,
                parcel.MerchantReference,
                merchant.Name,
                parcel.RecipientName,
                parcel.RecipientPhone,
                area.Name,
                hub.Name,
                parcel.Status,
                parcel.CodAmount,
                parcel.CollectedAmount,
                parcel.DeliveryCharge,
                parcel.Attempts,
                parcel.Created))
            .Skip((number - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);

        return new ParcelList(
            new Page<ParcelRow>([.. rows.Select(row => row with { Booked = tenant.Local(row.Booked) })], counts[query.Tab], number, size),
            counts);
    }

    private static IQueryable<Parcel> Filter(IQueryable<Parcel> parcels, ParcelQuery query, TenantInfo tenant)
    {
        if (query.MerchantId is { } merchantId)
        {
            parcels = parcels.Where(p => p.MerchantId == merchantId);
        }

        if (query.HubId is { } hubId)
        {
            parcels = parcels.Where(p => p.PickupHubId == hubId || p.DeliveryHubId == hubId);
        }

        if (query.From is { } from)
        {
            var start = tenant.StartUtc(from);
            parcels = parcels.Where(p => p.Created >= start);
        }

        if (query.To is { } to)
        {
            var end = tenant.StartUtc(to.AddDays(1));
            parcels = parcels.Where(p => p.Created < end);
        }

        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            var phone = PhoneNumber.Parse(search);
            if (phone.IsSuccess)
            {
                var value = phone.Value.Value;
                parcels = parcels.Where(p => p.RecipientPhone == value);
            }
            else
            {
                parcels = parcels.Where(p =>
                    p.TrackingCode == search ||
                    p.MerchantReference == search ||
                    p.RecipientName.Contains(search));
            }
        }

        return parcels;
    }
}
