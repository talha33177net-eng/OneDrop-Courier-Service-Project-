using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Application.Common;
using Domain.Common;
using Domain.Parcels;

namespace Application.Parcels.Stats;

/// <summary>The parcels in one status: how many, the cash on delivery they carry, and what was collected of it.</summary>
public sealed record StatusStat(ParcelStatus Status, int Count, decimal Cod, decimal Collected);

/// <summary>
/// The parcels booked in a period, by the status each is in now: how many were delivered, cancelled or came back, and
/// where the rest are. Every status is listed, in the order a parcel passes through them, with none left out.
/// </summary>
public sealed record ParcelStats(Period Period, IReadOnlyList<StatusStat> Statuses)
{
    public int Booked => Statuses.Sum(line => line.Count);

    /// <summary>The cash on delivery of everything booked.</summary>
    public decimal BookedCod => Statuses.Sum(line => line.Cod);

    /// <summary>Handed over at the door, in full or in part.</summary>
    public int Delivered => Count(ParcelStatus.Delivered, ParcelStatus.PartlyDelivered);

    /// <summary>Coming back to the merchant or handed back.</summary>
    public int Returned => Count(ParcelStatus.Returning, ParcelStatus.Returned);

    public int Cancelled => Count(ParcelStatus.Cancelled);

    /// <summary>The cash the riders took at the door.</summary>
    public decimal Collected => Statuses.Sum(line => line.Collected);

    /// <summary>Delivered out of the parcels that reached an end with the courier (delivered or sent back); null while none has.</summary>
    public double? DeliveryRate => Delivered + Returned == 0 ? null : (double)Delivered / (Delivered + Returned);

    public double? ReturnRate => Delivered + Returned == 0 ? null : (double)Returned / (Delivered + Returned);

    /// <summary>Cancelled out of everything booked; null while nothing was.</summary>
    public double? CancelRate => Booked == 0 ? null : (double)Cancelled / Booked;

    public int Count(params ParcelStatus[] statuses)
    {
        return Statuses.Where(line => statuses.Contains(line.Status)).Sum(line => line.Count);
    }

    /// <summary>The share of everything booked that <paramref name="count"/> parcels are.</summary>
    public double Share(int count)
    {
        return Booked == 0 ? 0 : (double)count / Booked;
    }
}

/// <summary>The parcels booked on one of the courier's days, and where they stand now.</summary>
public sealed record DayLine(DateOnly Day, int Booked, decimal Cod, int Delivered, int Returned, int Cancelled)
{
    public int OnTheWay => Booked - Delivered - Returned - Cancelled;
}

/// <summary>Every day of a period, newest first, with the parcels booked on it.</summary>
public sealed record ParcelDays(Period Period, IReadOnlyList<DayLine> Days);

/// <summary>
/// A merchant's stats: its parcels booked in a period (the courier's days), by the status they are in now, and day by
/// day. A merchant counts only its own parcels (the merchant filter).
/// </summary>
public class ParcelStatsHandler(IAppDbContext db, ITenantContext tenantContext, TimeProvider time)
{
    public async Task<Result<ParcelStats>> ForAsync(
        PeriodKind kind,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken = default)
    {
        var tenant = tenantContext.Require();
        var period = Period.Of(kind, tenant.Today(time.GetUtcNow().UtcDateTime), from, to);
        if (period.IsFailure)
        {
            return period.Error!;
        }

        var start = tenant.StartUtc(period.Value.From);
        var end = tenant.StartUtc(period.Value.To.AddDays(1));
        var byStatus = await db.Parcels
            .Where(p => p.Created >= start && p.Created < end)
            .GroupBy(p => p.Status)
            .Select(g => new
            {
                Status = g.Key,
                Count = g.Count(),
                Cod = g.Sum(p => p.CodAmount),
                Collected = g.Sum(p => (decimal?)p.CollectedAmount) ?? 0
            })
            .AsNoTracking()
            .ToDictionaryAsync(g => g.Status, cancellationToken);

        return new ParcelStats(
            period.Value,
            [
                .. Enum.GetValues<ParcelStatus>().Select(status => byStatus.TryGetValue(status, out var line)
                    ? new StatusStat(status, line.Count, line.Cod, line.Collected)
                    : new StatusStat(status, 0, 0, 0))
            ]);
    }

    /// <summary>The parcels booked on each day of the period, newest day first; a day with none is listed too.</summary>
    public async Task<Result<ParcelDays>> ByDayAsync(
        PeriodKind kind,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken = default)
    {
        var tenant = tenantContext.Require();
        var period = Period.Of(kind, tenant.Today(time.GetUtcNow().UtcDateTime), from, to);
        if (period.IsFailure)
        {
            return period.Error!;
        }

        // The booking time is UTC, so the day it belongs to is worked out here, on the courier's clock
        var start = tenant.StartUtc(period.Value.From);
        var end = tenant.StartUtc(period.Value.To.AddDays(1));
        var booked = await db.Parcels
            .Where(p => p.Created >= start && p.Created < end)
            .Select(p => new { p.Created, p.Status, p.CodAmount })
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var days = booked.GroupBy(p => tenant.Today(p.Created)).ToDictionary(day => day.Key);

        return new ParcelDays(
            period.Value,
            [
                .. Enumerable.Range(0, period.Value.Days)
                    .Select(back => period.Value.To.AddDays(-back))
                    .Select(day => days.TryGetValue(day, out var parcels)
                        ? new DayLine(
                            day,
                            parcels.Count(),
                            parcels.Sum(p => p.CodAmount),
                            parcels.Count(p => p.Status is ParcelStatus.Delivered or ParcelStatus.PartlyDelivered),
                            parcels.Count(p => p.Status is ParcelStatus.Returning or ParcelStatus.Returned),
                            parcels.Count(p => p.Status == ParcelStatus.Cancelled))
                        : new DayLine(day, 0, 0, 0, 0, 0))
            ]);
    }
}
