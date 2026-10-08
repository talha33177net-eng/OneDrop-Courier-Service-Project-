using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Application.Common;
using Domain.Delivery;
using Domain.Parcels;
using Domain.Payments;

namespace Application.Reports;

/// <summary>One day of the courier's work: what was finished and what the money did, on the courier's own clock.</summary>
public sealed record DayRow(
    DateOnly Day,
    int Delivered,
    int Returned,
    decimal Cod,
    decimal DeliveryCharges,
    decimal CodCharges,
    decimal ReturnCharges)
{
    /// <summary>What the courier keeps: every charge of the day.</summary>
    public decimal Earned => DeliveryCharges + CodCharges + ReturnCharges;

    /// <summary>The courier's adjustments of the day: positive credited merchants, negative charged them.</summary>
    public decimal Adjustments { get; init; }

    /// <summary>What the merchants are owed for the day: the cash less the charges, with the adjustments.</summary>
    public decimal OwedToMerchants => Cod - Earned + Adjustments;
}

/// <summary>One rider over the period: what they handed over, the cash they collected and what they handed in.</summary>
public sealed record RiderRow(
    string Rider,
    string Hub,
    int Runs,
    int Delivered,
    int Held,
    int Refused,
    decimal Collected,
    decimal Received,
    int OpenRuns)
{
    /// <summary>Cash the closed runs collected but did not hand in; negative when the rider handed in more.</summary>
    public decimal Short => Collected - Received;

    public int Stops => Delivered + Held + Refused;

    public double SuccessRate => Stops == 0 ? 0 : (double)Delivered / Stops;
}

/// <summary>One merchant over the period: how many of its parcels came back, and what the returns cost it.</summary>
public sealed record MerchantReturnRow(string Merchant, int Delivered, int Returned, decimal ReturnCharges)
{
    public int Finished => Delivered + Returned;

    public double ReturnRate => Finished == 0 ? 0 : (double)Returned / Finished;
}

/// <summary>Everything the reports page shows for the period asked for.</summary>
public sealed record CourierReport(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<DayRow> Days,
    IReadOnlyList<RiderRow> Riders,
    IReadOnlyList<MerchantReturnRow> Returns)
{
    public DayRow Total => new(
        To,
        Days.Sum(day => day.Delivered),
        Days.Sum(day => day.Returned),
        Days.Sum(day => day.Cod),
        Days.Sum(day => day.DeliveryCharges),
        Days.Sum(day => day.CodCharges),
        Days.Sum(day => day.ReturnCharges))
    {
        Adjustments = Days.Sum(day => day.Adjustments)
    };
}

/// <summary>
/// The courier's own reports over a period of days: the cash and charges day by day, each rider's deliveries and the
/// cash they handed in, and the returns each merchant had. Days are the courier's own (its time zone), so a report
/// lines up with its payouts; the money comes from the ledger, which is what a payout is made of, and the counts from
/// the parcels that reached a final status in the period.
/// </summary>
public class ReportsHandler(IAppDbContext db, ITenantContext tenantContext, TimeProvider time)
{
    /// <summary>The longest period a report covers, so one page can never read a whole year of parcels.</summary>
    public const int MaxDays = 92;

    public async Task<CourierReport> ForAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default)
    {
        var tenant = tenantContext.Require();
        var today = tenant.Today(time.GetUtcNow().UtcDateTime);
        var last = to ?? today;
        var first = from ?? last.AddDays(-6);
        if (last < first)
        {
            (first, last) = (last, first);
        }

        if (last.DayNumber - first.DayNumber >= MaxDays)
        {
            first = last.AddDays(-(MaxDays - 1));
        }

        return new CourierReport(
            first,
            last,
            await DaysAsync(first, last, cancellationToken),
            await RidersAsync(first, last, cancellationToken),
            await ReturnsAsync(first, last, cancellationToken));
    }

    private async Task<IReadOnlyList<DayRow>> DaysAsync(DateOnly first, DateOnly last, CancellationToken cancellationToken)
    {
        var money = await db.LedgerEntries
            .Where(entry => entry.EntryDate >= first && entry.EntryDate <= last)
            .GroupBy(entry => new { entry.EntryDate, entry.Kind })
            .Select(group => new { group.Key.EntryDate, group.Key.Kind, Amount = group.Sum(entry => entry.Amount) })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // A parcel's end is kept as UTC, so the day it belongs to is worked out here, on the courier's clock
        var tenant = tenantContext.Require();
        var fromUtc = tenant.StartUtc(first);
        var toUtc = tenant.StartUtc(last.AddDays(1));
        var finishedUtc = await db.Parcels
            .Where(parcel => parcel.ClosedOn >= fromUtc && parcel.ClosedOn < toUtc)
            .Select(parcel => new { parcel.ClosedOn, parcel.Status })
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var counts = finishedUtc
            .GroupBy(parcel => new { Day = DateOnly.FromDateTime(tenant.Local(parcel.ClosedOn!.Value)), parcel.Status })
            .Select(group => new { group.Key.Day, group.Key.Status, Count = group.Count() })
            .ToList();

        var days = new List<DayRow>();
        for (var day = first; day <= last; day = day.AddDays(1))
        {
            var lines = money.Where(line => line.EntryDate == day).ToList();
            var finished = counts.Where(count => count.Day == day).ToList();
            days.Add(new DayRow(
                day,
                finished.Where(c => c.Status is ParcelStatus.Delivered or ParcelStatus.PartlyDelivered).Sum(c => c.Count),
                finished.Where(c => c.Status == ParcelStatus.Returned).Sum(c => c.Count),
                lines.Where(line => line.Kind == LedgerEntryKind.Cod).Sum(line => line.Amount),
                // Charges are negative lines on a merchant's statement; a report reads them as what the courier earned
                -lines.Where(line => line.Kind == LedgerEntryKind.DeliveryCharge).Sum(line => line.Amount),
                -lines.Where(line => line.Kind == LedgerEntryKind.CodCharge).Sum(line => line.Amount),
                -lines.Where(line => line.Kind == LedgerEntryKind.ReturnCharge).Sum(line => line.Amount))
            {
                Adjustments = lines.Where(line => line.Kind == LedgerEntryKind.Adjustment).Sum(line => line.Amount)
            });
        }

        return days;
    }

    private async Task<IReadOnlyList<RiderRow>> RidersAsync(DateOnly first, DateOnly last, CancellationToken cancellationToken)
    {
        var runs = await db.DeliveryRuns
            .Where(run => run.RunDate >= first && run.RunDate <= last)
            .GroupBy(run => run.RiderId)
            .Select(group => new
            {
                RiderId = group.Key,
                Runs = group.Count(),
                Open = group.Count(run => run.Status == RunStatus.Open),
                Received = group.Sum(run => run.CashReceived ?? 0)
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        if (runs.Count == 0)
        {
            return [];
        }

        var stops = await (
            from attempt in db.DeliveryAttempts
            join run in db.DeliveryRuns on attempt.RunId equals run.Id
            where run.RunDate >= first && run.RunDate <= last
            group attempt by attempt.RiderId into byRider
            select new
            {
                RiderId = byRider.Key,
                Delivered = byRider.Count(a => a.Outcome == AttemptOutcome.Delivered || a.Outcome == AttemptOutcome.PartlyDelivered),
                Held = byRider.Count(a => a.Outcome == AttemptOutcome.Hold),
                Refused = byRider.Count(a => a.Outcome == AttemptOutcome.Refused),
                Collected = byRider.Sum(a => a.CollectedAmount)
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // Names after the aggregate: EF cannot group and then join in one query
        var riderIds = runs.Select(run => run.RiderId).ToList();
        var riders = await (
            from rider in db.Riders
            join hub in db.Hubs on rider.HubId equals hub.Id
            where riderIds.Contains(rider.Id)
            select new { rider.Id, rider.Name, Hub = hub.Name })
            .AsNoTracking()
            .ToDictionaryAsync(rider => rider.Id, cancellationToken);

        return
        [
            .. runs
                .Select(run =>
                {
                    var stop = stops.SingleOrDefault(s => s.RiderId == run.RiderId);
                    var rider = riders.GetValueOrDefault(run.RiderId);

                    return new RiderRow(
                        rider?.Name ?? "",
                        rider?.Hub ?? "",
                        run.Runs,
                        stop?.Delivered ?? 0,
                        stop?.Held ?? 0,
                        stop?.Refused ?? 0,
                        stop?.Collected ?? 0,
                        run.Received,
                        run.Open);
                })
                .OrderByDescending(row => row.Collected)
                .ThenBy(row => row.Rider)
        ];
    }

    private async Task<IReadOnlyList<MerchantReturnRow>> ReturnsAsync(
        DateOnly first,
        DateOnly last,
        CancellationToken cancellationToken)
    {
        var tenant = tenantContext.Require();
        var fromUtc = tenant.StartUtc(first);
        var toUtc = tenant.StartUtc(last.AddDays(1));
        var finished = await db.Parcels
            .Where(parcel => parcel.ClosedOn >= fromUtc && parcel.ClosedOn < toUtc &&
                (parcel.Status == ParcelStatus.Delivered || parcel.Status == ParcelStatus.PartlyDelivered ||
                    parcel.Status == ParcelStatus.Returned))
            .GroupBy(parcel => parcel.MerchantId)
            .Select(group => new
            {
                MerchantId = group.Key,
                Delivered = group.Count(parcel => parcel.Status != ParcelStatus.Returned),
                Returned = group.Count(parcel => parcel.Status == ParcelStatus.Returned)
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        if (finished.Count == 0)
        {
            return [];
        }

        var charges = await db.LedgerEntries
            .Where(entry => entry.EntryDate >= first && entry.EntryDate <= last && entry.Kind == LedgerEntryKind.ReturnCharge)
            .GroupBy(entry => entry.MerchantId)
            .Select(group => new { MerchantId = group.Key, Amount = group.Sum(entry => entry.Amount) })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var merchantIds = finished.Select(row => row.MerchantId).ToList();
        var names = await db.Merchants
            .IgnoreQueryFilters([QueryFilters.Merchant])
            .Where(merchant => merchantIds.Contains(merchant.Id))
            .Select(merchant => new { merchant.Id, merchant.Name })
            .AsNoTracking()
            .ToDictionaryAsync(merchant => merchant.Id, merchant => merchant.Name, cancellationToken);

        return
        [
            .. finished
                .Select(row => new MerchantReturnRow(
                    names.GetValueOrDefault(row.MerchantId, ""),
                    row.Delivered,
                    row.Returned,
                    -charges.SingleOrDefault(charge => charge.MerchantId == row.MerchantId)?.Amount ?? 0))
                .Where(row => row.Finished > 0)
                .OrderByDescending(row => row.Returned)
                .ThenByDescending(row => row.Finished)
                .ThenBy(row => row.Merchant)
        ];
    }
}
