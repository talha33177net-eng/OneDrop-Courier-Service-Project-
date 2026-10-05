using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Application.Common;
using Application.Parcels.Browse;
using Domain.Delivery;
using Domain.Merchants;
using Domain.Parcels;
using Domain.Payments;

namespace Application.Dashboards;

/// <summary>Parcels booked, delivered and returned on one of the tenant's days.</summary>
public sealed record DayCount(DateOnly Day, int Booked, int Delivered, int Returned);

/// <summary>How many parcels are in each stage of their life.</summary>
public sealed record StageCounts(int Pending, int InTransit, int OutForDelivery, int OnHold, int Delivered, int Returns, int Cancelled)
{
    public int Total => Pending + InTransit + OutForDelivery + OnHold + Delivered + Returns + Cancelled;

    public int Active => Pending + InTransit + OutForDelivery + OnHold;

    /// <summary>Delivered out of the parcels that have ended (delivered or returned), as a whole per cent.</summary>
    public int? SuccessRate => Delivered + Returns == 0 ? null : (int)Math.Round(100m * Delivered / (Delivered + Returns));
}

public sealed record MerchantDashboard(
    string Merchant,
    MerchantStatus Status,
    bool HasPayoutAccount,
    StageCounts Stages,
    decimal CollectedLast30Days,
    decimal Unpaid,
    decimal PaidThisMonth,
    IReadOnlyList<DayCount> Week,
    IReadOnlyList<ParcelRow> Recent)
{
    /// <summary>A pickup is requested or a rider is on the way.</summary>
    public bool PickupOpen { get; init; }

    /// <summary>The merchant has asked for a pickup or handed a parcel over at least once.</summary>
    public bool HandedOver { get; init; }

    /// <summary>Parcels still on their way after the day they were due.</summary>
    public int Late { get; init; }
}

public sealed record HubRow(string Code, string Name, int AtHub, int ToAssign, int Incoming, int WithRiders);

public sealed record TopMerchant(string Name, int Parcels, int Delivered);

public sealed record AdminDashboard(
    int BookedToday,
    int PickedUpToday,
    int DeliveredToday,
    int ReturnedToday,
    StageCounts Stages,
    int PendingMerchants,
    int OpenPickups,
    decimal CashWithRiders,
    decimal OwedToMerchants,
    IReadOnlyList<DayCount> Week,
    IReadOnlyList<HubRow> Hubs,
    IReadOnlyList<TopMerchant> TopMerchants)
{
    /// <summary>Parcels still on their way after the day they were due.</summary>
    public int Late { get; init; }
}

/// <summary>
/// The numbers on the merchant's and the admin's dashboards, worked out from the data when asked. A merchant's are its
/// own (the merchant filter); the admin's are the whole courier's.
/// </summary>
public class DashboardHandler(
    IAppDbContext db,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    ParcelListHandler parcels,
    TimeProvider time)
{
    public const int WeekDays = 7;

    public async Task<MerchantDashboard> MerchantAsync(CancellationToken cancellationToken = default)
    {
        var tenant = tenantContext.Require();
        var merchantId = currentUser.MerchantId ?? throw new InvalidOperationException("A merchant reads its dashboard.");
        var merchant = await db.Merchants.AsNoTracking().SingleAsync(m => m.Id == merchantId, cancellationToken);
        var today = tenant.Today(time.GetUtcNow().UtcDateTime);
        var since30 = tenant.StartUtc(today.AddDays(-29));
        var monthStart = tenant.StartUtc(new DateOnly(today.Year, today.Month, 1));

        var collected = await db.Parcels
            .Where(p => p.ClosedOn >= since30 && p.CollectedAmount != null)
            .SumAsync(p => p.CollectedAmount, cancellationToken) ?? 0;
        var unpaid = await db.LedgerEntries.Where(e => e.PayoutId == null).SumAsync(e => (decimal?)e.Amount, cancellationToken) ?? 0;
        var paid = await db.Payouts
            .Where(p => p.Status == PayoutStatus.Paid && p.PaidOn >= monthStart)
            .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0;
        var recent = await parcels.ListAsync(new ParcelQuery { PageSize = 8 }, cancellationToken);
        var pickupOpen = await db.PickupRequests.AnyAsync(
            r => r.Status == PickupStatus.Requested || r.Status == PickupStatus.Assigned,
            cancellationToken);
        var handedOver = pickupOpen ||
            await db.PickupRequests.AnyAsync(cancellationToken) ||
            await db.Parcels.AnyAsync(p => p.Status != ParcelStatus.Pending && p.Status != ParcelStatus.Cancelled, cancellationToken);

        return new MerchantDashboard(
            merchant.Name,
            merchant.Status,
            merchant.HasPayoutAccount,
            await StagesAsync(cancellationToken),
            collected,
            unpaid,
            paid,
            await WeekAsync(tenant, today, cancellationToken),
            recent.Page.Items)
        {
            PickupOpen = pickupOpen,
            HandedOver = handedOver,
            Late = await LateAsync(today, cancellationToken)
        };
    }

    public async Task<AdminDashboard> AdminAsync(CancellationToken cancellationToken = default)
    {
        var tenant = tenantContext.Require();
        var today = tenant.Today(time.GetUtcNow().UtcDateTime);
        var todayStart = tenant.StartUtc(today);

        var bookedToday = await db.Parcels.CountAsync(p => p.Created >= todayStart, cancellationToken);
        var pickedToday = await db.ParcelEvents
            .Where(e => e.Status == ParcelStatus.PickedUp && e.Created >= todayStart)
            .Select(e => e.ParcelId)
            .Distinct()
            .CountAsync(cancellationToken);
        var deliveredToday = await db.Parcels.CountAsync(
            p => p.ClosedOn >= todayStart && (p.Status == ParcelStatus.Delivered || p.Status == ParcelStatus.PartlyDelivered),
            cancellationToken);
        var returnedToday = await db.Parcels.CountAsync(p => p.ClosedOn >= todayStart && p.Status == ParcelStatus.Returned, cancellationToken);
        var pendingMerchants = await db.Merchants.CountAsync(m => m.Status == MerchantStatus.Pending && !m.Archived, cancellationToken);
        var openPickups = await db.PickupRequests.CountAsync(
            r => r.Status == PickupStatus.Requested || r.Status == PickupStatus.Assigned,
            cancellationToken);
        var cash = await (
            from attempt in db.DeliveryAttempts
            join run in db.DeliveryRuns on attempt.RunId equals run.Id
            where run.Status == RunStatus.Open
            select (decimal?)attempt.CollectedAmount)
            .SumAsync(cancellationToken) ?? 0;
        var owed = await db.LedgerEntries.Where(e => e.PayoutId == null).SumAsync(e => (decimal?)e.Amount, cancellationToken) ?? 0;

        var hubs = await db.Hubs
            .Where(h => !h.Archived)
            .OrderBy(h => h.Name)
            .Select(h => new HubRow(
                h.Code,
                h.Name,
                db.Parcels.Count(p => p.CurrentHubId == h.Id),
                db.Parcels.Count(p => p.CurrentHubId == h.Id && p.DeliveryHubId == h.Id &&
                    (p.Status == ParcelStatus.AtHub || p.Status == ParcelStatus.OnHold)),
                db.Parcels.Count(p => p.TransferToHubId == h.Id),
                (from a in db.DeliveryAttempts
                    join run in db.DeliveryRuns on a.RunId equals run.Id
                    where run.HubId == h.Id && a.Outcome == null
                    select a.Id).Count()))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var monthStart = tenant.StartUtc(new DateOnly(today.Year, today.Month, 1));
        var busiest = await db.Parcels
            .Where(parcel => parcel.Created >= monthStart)
            .GroupBy(parcel => parcel.MerchantId)
            .Select(parcels => new
            {
                MerchantId = parcels.Key,
                Booked = parcels.Count(),
                Delivered = parcels.Count(p => p.Status == ParcelStatus.Delivered || p.Status == ParcelStatus.PartlyDelivered)
            })
            .OrderByDescending(m => m.Booked)
            .Take(5)
            .ToListAsync(cancellationToken);
        var busiestIds = busiest.Select(m => m.MerchantId).ToList();
        var names = await db.Merchants
            .Where(m => busiestIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, m => m.Name, cancellationToken);
        var top = busiest.Select(m => new TopMerchant(names[m.MerchantId], m.Booked, m.Delivered)).ToList();

        return new AdminDashboard(
            bookedToday,
            pickedToday,
            deliveredToday,
            returnedToday,
            await StagesAsync(cancellationToken),
            pendingMerchants,
            openPickups,
            cash,
            owed,
            await WeekAsync(tenant, today, cancellationToken),
            hubs.Where(h => h.AtHub + h.Incoming + h.WithRiders > 0).ToList() is { Count: > 0 } busy ? busy : hubs.Take(6).ToList(),
            top)
        {
            Late = await LateAsync(today, cancellationToken)
        };
    }

    private async Task<int> LateAsync(DateOnly today, CancellationToken cancellationToken)
    {
        return await db.Parcels.CountAsync(p => p.DueOn < today && ParcelStatuses.ToDeliver.Contains(p.Status), cancellationToken);
    }

    private async Task<StageCounts> StagesAsync(CancellationToken cancellationToken)
    {
        var byStatus = await db.Parcels
            .GroupBy(p => p.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Status, g => g.Count, cancellationToken);

        int Count(ParcelTab tab)
        {
            return ParcelListHandler.StatusesOf(tab).Sum(status => byStatus.GetValueOrDefault(status));
        }

        return new StageCounts(
            Count(ParcelTab.Pending),
            Count(ParcelTab.InTransit),
            Count(ParcelTab.OutForDelivery),
            Count(ParcelTab.OnHold),
            Count(ParcelTab.Delivered),
            Count(ParcelTab.Returns),
            Count(ParcelTab.Cancelled));
    }

    private async Task<IReadOnlyList<DayCount>> WeekAsync(TenantInfo tenant, DateOnly today, CancellationToken cancellationToken)
    {
        var first = today.AddDays(1 - WeekDays);
        var since = tenant.StartUtc(first);
        var rows = await db.Parcels
            .Where(p => p.Created >= since || p.ClosedOn >= since)
            .Select(p => new { p.Created, p.ClosedOn, p.Status })
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var booked = rows.Where(r => r.Created >= since).GroupBy(r => tenant.Today(r.Created)).ToDictionary(g => g.Key, g => g.Count());
        var closed = rows
            .Where(r => r.ClosedOn >= since)
            .GroupBy(r => tenant.Today(r.ClosedOn!.Value))
            .ToDictionary(g => g.Key, g => g.ToList());

        return
        [
            .. Enumerable.Range(0, WeekDays)
                .Select(first.AddDays)
                .Select(day => new DayCount(
                    day,
                    booked.GetValueOrDefault(day),
                    closed.GetValueOrDefault(day)?.Count(r => r.Status is ParcelStatus.Delivered or ParcelStatus.PartlyDelivered) ?? 0,
                    closed.GetValueOrDefault(day)?.Count(r => r.Status == ParcelStatus.Returned) ?? 0))
        ];
    }
}
