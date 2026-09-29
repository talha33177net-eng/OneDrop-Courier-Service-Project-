using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Domain.Common;
using Domain.Delivery;
using Domain.Payments;

namespace Application.Delivery.HubCash;

/// <summary>
/// A rider's trip and its cash: <see cref="Cash"/> is what the rider collected in cash (wallet payments are already with
/// the operator), <see cref="HandedIn"/> what hub staff received, null until then. <see cref="Id"/> names the trip in
/// the page's form only.
/// </summary>
public sealed record CashTripRow(
    long Id,
    string Rider,
    DateOnly Day,
    TripStatus Status,
    decimal Cash,
    decimal? HandedIn,
    DateTime? HandedInOn)
{
    /// <summary>What the rider handed in less than they collected; negative when they handed in more.</summary>
    public decimal? Short => Cash - HandedIn;
}

/// <summary>The hub's riders' cash: today's trips, and earlier ones whose cash has not been handed in.</summary>
public sealed record HubCash(DateOnly Day, IReadOnlyList<CashTripRow> Trips)
{
    public decimal StillWithRiders => Trips.Where(trip => trip.HandedIn is null).Sum(trip => trip.Cash);
}

/// <summary>
/// The riders' end-of-day cash at a hub. A rider back from a trip with every stop done hands in the cash they
/// collected; hub staff count it and record what they received. The expected amount is the trip's cash payments, so a
/// shortfall shows at once. Shops are paid the next day whatever the count (owner's choice): the shortfall is the
/// hub's to follow up, not the shop's.
/// </summary>
public class HubCashHandler(IAppDbContext db, ITenantContext tenantContext, TimeProvider time)
{
    /// <summary>Null for a hub that is not this operator's.</summary>
    public async Task<HubCash?> ListAsync(string hubCode, CancellationToken cancellationToken = default)
    {
        var hubId = await HubIdAsync(hubCode, cancellationToken);
        if (hubId is null)
        {
            return null;
        }

        var today = Today();
        var trips = await (
            from trip in db.Trips
            join rider in db.Riders on trip.RiderId equals rider.Id
            where trip.HubId == hubId &&
                (trip.Status == TripStatus.Out || trip.Status == TripStatus.Finished) &&
                (trip.DeliveryDate == today || trip.CashReceivedOn == null)
            orderby trip.DeliveryDate descending, rider.Name
            select new
            {
                trip.Id,
                rider.Name,
                trip.DeliveryDate,
                trip.Status,
                trip.CashExpected,
                trip.CashReceived,
                trip.CashReceivedOn,
                Cash = db.Payments
                    .Where(p => p.TripId == trip.Id && p.Method == PaymentMethod.Cash && p.Status == PaymentStatus.Paid)
                    .Sum(p => (decimal?)(p.Fee + p.Cod)) ?? 0
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return new HubCash(
            today,
            [
                .. trips
                    .Where(trip => trip.Cash > 0 || trip.CashReceivedOn is not null)
                    .Select(trip => new CashTripRow(
                        trip.Id,
                        trip.Name,
                        trip.DeliveryDate,
                        trip.Status,
                        trip.CashExpected ?? trip.Cash,
                        trip.CashReceived,
                        trip.CashReceivedOn))
            ]);
    }

    /// <summary>
    /// Records <paramref name="received"/> as the cash handed in for trip <paramref name="tripId"/> of the hub. Refused
    /// while the rider has stops to do, and once already recorded.
    /// </summary>
    public async Task<Result<CashTripRow>> HandInAsync(
        string hubCode,
        long tripId,
        decimal received,
        CancellationToken cancellationToken = default)
    {
        var hubId = await HubIdAsync(hubCode, cancellationToken);
        var trip = hubId is null
            ? null
            : await db.Trips.FirstOrDefaultAsync(t => t.Id == tripId && t.HubId == hubId, cancellationToken);
        if (trip is null)
        {
            return Error.NotFound("trip.cash.none", "There is no such trip at this hub.");
        }

        var cash = await db.Payments
            .Where(p => p.TripId == trip.Id && p.Method == PaymentMethod.Cash && p.Status == PaymentStatus.Paid)
            .SumAsync(p => p.Fee + p.Cod, cancellationToken);
        var handedIn = trip.HandInCash(cash, received, time.GetUtcNow().UtcDateTime);
        if (handedIn.IsFailure)
        {
            return handedIn.Error!;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict("trip.cash.changed", "This trip changed while you were counting. Open the page again.");
        }

        var rider = await db.Riders.Where(r => r.Id == trip.RiderId).Select(r => r.Name).SingleAsync(cancellationToken);

        return new CashTripRow(trip.Id, rider, trip.DeliveryDate, trip.Status, cash, received, trip.CashReceivedOn);
    }

    private async Task<long?> HubIdAsync(string hubCode, CancellationToken cancellationToken)
    {
        return await db.Hubs
            .Where(h => h.Code == hubCode && !h.Archived)
            .Select(h => (long?)h.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private DateOnly Today()
    {
        var tenant = tenantContext.Tenant ?? throw new InvalidOperationException("Hub cash needs a tenant.");

        return TimeZoneInfo.FindSystemTimeZoneById(tenant.TimeZone).LocalDay(time.GetUtcNow().UtcDateTime);
    }
}
