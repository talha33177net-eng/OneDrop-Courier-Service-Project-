using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Domain.Common;
using Domain.Customers;
using Domain.Delivery;
using Domain.Grouping;
using Domain.Orders;
using Domain.Payments;
using Domain.Pricing;

namespace Application.Delivery.RiderDay;

/// <summary>
/// One shop's order at a stop. <see cref="Ready"/> while the trip is planned: every parcel is on the shelf. Once the
/// trip has left, only the orders the rider took are listed, with what became of them at the door.
/// </summary>
public sealed record RiderStopOrder(string Number, string Shop, OrderStatus Status, IReadOnlyList<string> Labels, bool Ready);

/// <summary>
/// A stop on the rider's trip: one door, where the customer takes every delivery the trip has for them in that area
/// and pays one fee (<see cref="Fee"/>, on what is handed over, with the shops' cash on delivery). <see cref="Key"/>
/// names the stop in the rider's forms. <see cref="Outcome"/> is null until the rider has been there; then
/// <see cref="Fee"/> and <see cref="Cod"/> are what was collected and <see cref="PaidBy"/> how it was paid.
/// </summary>
public sealed record RiderStop(
    int Number,
    string Key,
    IReadOnlyList<string> Deliveries,
    StopOutcome? Outcome,
    IReadOnlyList<string> Shelves,
    string Recipient,
    string Phone,
    string Address,
    string? Landmark,
    string Area,
    IReadOnlyList<RiderStopOrder> Orders,
    decimal Fee,
    decimal Cod,
    PaymentMethod? PaidBy = null)
{
    public decimal ToCollect => Fee + Cod;

    public int Parcels => Orders.Sum(order => order.Labels.Count);
}

/// <summary>
/// The rider's day: their trip for today (none yet while <see cref="Status"/> is null) and its stops, neighbours
/// together, against the bike's limit. <see cref="Cash"/> is the cash collected so far, which the rider hands in at
/// the hub (the rest was paid by bKash or Nagad); <see cref="CashHandedIn"/> is what hub staff received from them, null
/// until then.
/// </summary>
public sealed record RiderToday(
    string Rider,
    string Hub,
    DateOnly Day,
    TripStatus? Status,
    TripLoad Limit,
    IReadOnlyList<RiderStop> Stops,
    decimal Cash = 0,
    decimal? CashHandedIn = null)
{
    /// <summary>What the stops still to do will collect.</summary>
    public decimal ToCollect => Stops.Where(stop => stop.Outcome is null).Sum(stop => stop.ToCollect);

    /// <summary>What the rider has collected so far, in cash and by wallet.</summary>
    public decimal Collected => Stops.Where(stop => stop.Outcome is not null).Sum(stop => stop.ToCollect);
}

/// <summary>
/// What starting the trip did: deliveries taken out, deliveries left at the hub with nothing ready, and orders not
/// ready of a delivery that went out, which follow in a later delivery.
/// </summary>
public sealed record StartedTrip(int Deliveries, int Orders, int LeftBehind, int OrdersFollowing);

/// <summary>
/// The signed-in rider's screen. Starting the trip hands over every order whose parcels are all on the shelf
/// (<see cref="Order.HandToRider"/>): its delivery goes out and frees its shelf. An order not ready moves to a later
/// delivery (<see cref="Order.FollowUpIn"/>): the customer's open one to that address, or a follow-up delivered the
/// next day at the extra-shop fee. A delivery with nothing ready comes off the trip and waits for the next one.
/// </summary>
public class RiderDayHandler(IAppDbContext db, ITenantContext tenantContext, TimeProvider time)
{
    public static Error NoTrip => Error.NotFound("trip.none", "You have no trip today.");

    /// <summary>Null when the user is not one of the operator's riders.</summary>
    public async Task<RiderToday?> TodayAsync(long userId, CancellationToken cancellationToken = default)
    {
        var (tenant, timeZone) = TenantAndTimeZone();
        var today = timeZone.LocalDay(time.GetUtcNow().UtcDateTime);
        var (rider, trip) = await db.TodaysTripAsync(userId, today, cancellationToken);
        if (rider is null)
        {
            return null;
        }

        var hub = await db.Hubs.AsNoTracking().SingleAsync(h => h.Id == rider.HubId, cancellationToken);
        if (trip is null)
        {
            return new RiderToday(rider.Name, hub.Name, today, null, rider.Limit, []);
        }

        var visits = await db.VisitsAsync(trip.Id, cancellationToken);
        var merchantIds = visits
            .SelectMany(visit => visit.Deliveries)
            .SelectMany(delivery => delivery.Orders)
            .Select(order => order.MerchantId)
            .Distinct()
            .ToList();
        var shops = await db.Merchants
            .Where(merchant => merchantIds.Contains(merchant.Id))
            .ToDictionaryAsync(merchant => merchant.Id, merchant => merchant.Name, cancellationToken);
        var fees = new DeliveryFeeCalculator(tenant.Fees);
        var paid = await db.Payments
            .AsNoTracking()
            .Where(p => p.TripId == trip.Id && p.Status == PaymentStatus.Paid)
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        // Fees paid in advance (3.6b) come off what is still to collect at the door
        var deliveryIds = visits.SelectMany(visit => visit.Deliveries).Select(delivery => delivery.Group.Id).ToList();
        var advances = await db.Payments
            .AsNoTracking()
            .Where(p => deliveryIds.Contains(p.DeliveryGroupId) &&
                p.Purpose == PaymentPurpose.Advance &&
                p.Status == PaymentStatus.Paid)
            .GroupBy(p => p.DeliveryGroupId)
            .Select(delivery => new { Id = delivery.Key, Paid = delivery.Sum(p => p.Fee) })
            .ToDictionaryAsync(delivery => delivery.Id, delivery => delivery.Paid, cancellationToken);

        return new RiderToday(
            rider.Name,
            hub.Name,
            today,
            trip.Status,
            rider.Limit,
            [.. visits.Select((visit, index) => Describe(visit, index + 1))],
            paid.Values.Where(p => p.Method == PaymentMethod.Cash).Sum(p => p.Amount),
            trip.CashReceived);

        RiderStop Describe(Visit visit, int number)
        {
            var carried = visit.Deliveries
                .Select(delivery => (delivery.Group.Kind, Orders: delivery.Carried(trip.Status).ToList()))
                .ToList();

            // Still to do: what the door will cost for what is carried; once done, what was collected
            var due = carried
                .Select(delivery => (delivery.Kind, Orders: delivery.Orders.Where(order => Order.IsForDelivery(order.Status)).ToList()))
                .ToList();
            var fee = visit.Outcome is null
                ? fees.VisitFees(
                    [
                        .. due.Select(delivery => new FeeDelivery(
                            delivery.Kind,
                            [.. delivery.Orders.Select(FeeLine.Of)]))
                    ])
                    .Select((share, index) =>
                        Math.Max(0, share - advances.GetValueOrDefault(visit.Deliveries[index].Group.Id)))
                    .Sum()
                : visit.Deliveries.Sum(delivery => delivery.Stop.FeeCollected ?? 0);
            var cod = visit.Outcome is null
                ? due.Sum(delivery => delivery.Orders.Sum(order => order.CodAmount))
                : visit.Deliveries.Sum(delivery => delivery.Stop.CodCollected ?? 0);
            var orders = carried
                .SelectMany(delivery => delivery.Orders)
                .Select(order => (Order: order, Shop: shops[order.MerchantId]))
                .OrderBy(row => row.Shop)
                .ThenBy(row => row.Order.Number)
                .ToList();

            return new RiderStop(
                number,
                visit.Key,
                [.. visit.Deliveries.Select(delivery => delivery.Group.Number)],
                visit.Outcome,
                [
                    .. visit.Deliveries
                        .Where(delivery => delivery.Group.Shelf is not null)
                        .Select(delivery => DeliveryGroup.ShelfCode(hub.Code, delivery.Group.Shelf!.Value))
                ],
                string.Join(" / ", orders.Select(row => row.Order.RecipientName).Distinct().DefaultIfEmpty(visit.Place.Name ?? "")),
                PhoneNumber.Parse(visit.Place.Phone).Value.Local,
                visit.Place.Address,
                visit.Place.Landmark,
                visit.Place.Area,
                [
                    .. orders.Select(row => new RiderStopOrder(
                        row.Order.Number,
                        row.Shop,
                        row.Order.Status,
                        [.. row.Order.Packages.OrderBy(p => p.Sequence).Select(p => new PackageLabel(row.Order.Number, p.Sequence).ToString())],
                        row.Order.IsReadyAt(hub.Id)))
                ],
                fee,
                cod,
                visit.Deliveries[0].Stop.PaymentId is { } paymentId ? paid[paymentId].Method : null);
        }
    }

    /// <summary>
    /// The rider leaves with every order ready on the shelves; orders not ready of a delivery that goes out follow in
    /// a later delivery. Refused when nothing on the trip is ready.
    /// </summary>
    public async Task<Result<StartedTrip>> StartAsync(long userId, CancellationToken cancellationToken = default)
    {
        var (tenant, timeZone) = TenantAndTimeZone();
        var now = time.GetUtcNow().UtcDateTime;
        var (rider, trip) = await db.TodaysTripAsync(userId, timeZone.LocalDay(now), cancellationToken);
        if (rider is null || trip is null)
        {
            return NoTrip;
        }

        if (trip.Status != TripStatus.Planned)
        {
            return Trip.NotPlanned;
        }

        var stops = await db.TripStops.Where(stop => stop.TripId == trip.Id).ToListAsync(cancellationToken);
        var groupIds = stops.Select(stop => stop.DeliveryGroupId).ToList();
        var groups = await db.DeliveryGroups.Where(g => groupIds.Contains(g.Id)).ToListAsync(cancellationToken);
        var orders = await db.Orders
            .Include(order => order.Packages)
            .Where(order => groupIds.Contains(order.DeliveryGroupId))
            .ToListAsync(cancellationToken);

        var taken = 0;
        var leftBehind = 0;
        var ordersFollowing = 0;
        var following = new List<(DeliveryGroup Later, int? Shelf)>();
        foreach (var stop in stops)
        {
            var group = groups.Single(g => g.Id == stop.DeliveryGroupId);
            var inGroup = orders.Where(order => order.DeliveryGroupId == group.Id).ToList();
            var handed = inGroup.Count(order => order.HandToRider(trip.HubId));
            if (handed == 0)
            {
                db.TripStops.Remove(stop);
                leftBehind++;

                continue;
            }

            var shelf = group.Shelf;
            group.MoveTo(DeliveryGroupStatus.Dispatched, now);
            taken += handed;

            var notReady = inGroup
                .Where(order => Order.IsForDelivery(order.Status) && order.Status != OrderStatus.OutForDelivery)
                .ToList();
            if (notReady.Count == 0)
            {
                continue;
            }

            var later = await LaterDeliveryAsync(group, now, (tenant, timeZone), cancellationToken);
            foreach (var order in notReady)
            {
                // The shop pays for the second trip when it had not handed the order over, once per order; not when
                // the parcel was already with us, nor when it waited at the shop for the customer's advance
                var shopLate = order.Status == OrderStatus.Created && !order.WaitsForAdvance && order.LeftBehindOn is null;
                order.FollowUpIn(later, now);
                if (shopLate && tenant.LateHandoverFee > 0)
                {
                    db.LedgerEntries.Add(LedgerEntry.LateHandoverFee(order, tenant.LateHandoverFee, trip.DeliveryDate));
                }
            }

            ordersFollowing += notReady.Count;

            // Parcels already here stay where they are: the follow-up takes over the shelf the delivery frees
            if (notReady.Any(order => order.Packages.Any(p => p.HubId == trip.HubId)))
            {
                following.Add((later, shelf));
            }
        }

        if (taken == 0)
        {
            return Error.Conflict("trip.nothingReady", "No parcel on this trip is on its shelf yet.");
        }

        trip.Start(now);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict("trip.changed", "The trip changed while you were starting it. Open it again.");
        }

        await ShelveFollowUpsAsync(trip.HubId, following, cancellationToken);

        return new StartedTrip(
            stops.Count - leftBehind,
            taken,
            leftBehind,
            ordersFollowing);
    }

    /// <summary>
    /// Where orders not ready go when their delivery leaves: the customer's open delivery to the same address if it
    /// can still take them, else a new follow-up delivered the next day.
    /// </summary>
    private async Task<DeliveryGroup> LaterDeliveryAsync(
        DeliveryGroup group,
        DateTime now,
        (TenantInfo Tenant, TimeZoneInfo TimeZone) settings,
        CancellationToken cancellationToken)
    {
        var open = await db.DeliveryGroups.FirstOrDefaultAsync(
            g => g.CustomerId == group.CustomerId && g.AddressId == group.AddressId && g.Status == DeliveryGroupStatus.Open,
            cancellationToken);
        if (open is not null && open.CanJoin(now))
        {
            return open;
        }

        var followUp = DeliveryGroup.FollowUp(new NewDeliveryGroup(
            group.CustomerId,
            group.AddressId,
            group.HubId,
            now,
            settings.TimeZone,
            settings.Tenant.GroupJoinDays));
        db.DeliveryGroups.Add(followUp);

        return followUp;
    }

    /// <summary>
    /// Puts each follow-up whose parcels are already at the hub on the shelf its delivery freed, or the lowest free
    /// one if a scan took that meanwhile. A shelf lost to a scan at the same moment is left to the next scan-in.
    /// </summary>
    private async Task ShelveFollowUpsAsync(
        long hubId,
        IReadOnlyList<(DeliveryGroup Later, int? Shelf)> following,
        CancellationToken cancellationToken)
    {
        foreach (var (later, freed) in following.Where(f => f.Later.NeedsShelf))
        {
            var taken = (await db.DeliveryGroups
                .Where(g => g.HubId == hubId && g.Shelf != null)
                .Select(g => g.Shelf!.Value)
                .ToListAsync(cancellationToken)).ToHashSet();
            var shelf = freed is { } kept && !taken.Contains(kept)
                ? kept
                : Enumerable.Range(1, taken.Count + 1).First(n => !taken.Contains(n));
            later.PutOnShelf(shelf);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                await db.Entry(later).ReloadAsync(cancellationToken);
            }
        }
    }

    private (TenantInfo Tenant, TimeZoneInfo TimeZone) TenantAndTimeZone()
    {
        var tenant = tenantContext.Tenant ?? throw new InvalidOperationException("Riders need a tenant.");

        return (tenant, TimeZoneInfo.FindSystemTimeZoneById(tenant.TimeZone));
    }
}
