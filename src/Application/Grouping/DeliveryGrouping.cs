using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Domain.Customers;
using Domain.Grouping;
using Domain.Orders;
using Domain.Payments;
using Domain.Pricing;

namespace Application.Grouping;

/// <summary>
/// A would-be order, for <see cref="DeliveryGrouping.QuoteAsync"/>. The ids are null for a customer or address not
/// seen yet; <see cref="PickupPointId"/> is the shop's pickup point the parcels would be collected from.
/// </summary>
public sealed record QuoteRequest(
    long MerchantId,
    long? CustomerId,
    long? AddressId,
    long PickupPointId,
    DeliverySpeed Speed,
    bool DoNotHold,
    int WeightGrams);

/// <summary>What the would-be order adds to the delivery fee, and whether it joins a delivery already on its way.</summary>
public sealed record DeliveryQuote(decimal Fee, bool JoinsDelivery);

/// <summary>
/// Puts a new order in its delivery group and saves them together. The order joins the customer's delivery to the
/// address that leaves soonest and can still take it (<see cref="DeliveryGroup.CanTake"/>): the open group, or a
/// next-day delivery whose day the order's pickup route still reaches. Otherwise an order that waits opens a group,
/// and a Deliver fast or Don't hold order opens a next-day delivery of its own. The window (join days, time zone)
/// is the tenant's. Two orders opening the same customer's first group at the same moment are settled by the unique
/// index on open groups: the loser joins the winner's group. The order is priced for the group it lands in
/// (<see cref="Order.AddedFee"/>), at the tenant's prices.
/// </summary>
public class DeliveryGrouping(IAppDbContext db, ITenantContext tenantContext, TimeProvider time)
{
    /// <summary>
    /// Adds <paramref name="order"/> (not yet saved) and saves it in its group. A failure that is not the open-group
    /// race, e.g. a repeated Idempotency-Key, is thrown for the caller.
    /// </summary>
    public async Task SaveInGroupAsync(Order order, long hubId, CancellationToken cancellationToken)
    {
        var tenant = tenantContext.Tenant ?? throw new InvalidOperationException("Grouping needs a tenant.");
        var now = time.GetUtcNow().UtcDateTime;
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(tenant.TimeZone);
        var spec = new NewDeliveryGroup(order.CustomerId, order.AddressId, hubId, now, timeZone, tenant.GroupJoinDays);
        var fees = new DeliveryFeeCalculator(tenant.Fees);
        var line = new FeeLine(order.MerchantId, order.Speed, order.Status, order.TotalWeightGrams);

        if (order.WaitsForGroup)
        {
            await LockOverdueAsync(order, now, cancellationToken);
        }

        var pickup = await NextPickupAsync(order.PickupPointId, now, timeZone, cancellationToken);
        var group = await FindSoonestAsync(
                order.CustomerId,
                order.AddressId,
                g => g.CanTake(now, pickup, order.WaitsForGroup, timeZone),
                cancellationToken)
            ?? (order.WaitsForGroup ? DeliveryGroup.Open(spec) : DeliveryGroup.OpenAlone(spec));
        order.PlaceIn(group, await AddedFeeAsync(group, line, fees, cancellationToken));
        await MatchAdvanceAsync(order, group, now, cancellationToken);
        await AskAboutAddressAsync(order, cancellationToken);
        db.Orders.Add(order);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (group.Status == DeliveryGroupStatus.Open &&
            db.Entry(group).State == EntityState.Added)
        {
            // Another order opened this customer's group first: join it. If there is none, the failure was
            // something else (the order's own unique keys) and the caller handles it.
            var winner = await FindOpenAsync(order, cancellationToken);
            if (winner is null || !winner.CanJoin(now))
            {
                throw;
            }

            // Move the order before dropping the unsaved group: detaching a group an order still points at
            // would sever a required relationship
            order.PlaceIn(winner, await AddedFeeAsync(winner, line, fees, cancellationToken));
            await MatchAdvanceAsync(order, winner, now, cancellationToken);
            db.Entry(order).DetectChanges();
            db.Entry(group).State = EntityState.Detached;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// What an order would add to the customer's delivery fee if it were placed now: the checkout quote. The group
    /// is chosen as <see cref="SaveInGroupAsync"/> would choose it, but nothing is changed or saved. A customer or
    /// address not seen before opens a new group.
    /// </summary>
    public async Task<DeliveryQuote> QuoteAsync(QuoteRequest request, CancellationToken cancellationToken)
    {
        var tenant = tenantContext.Tenant ?? throw new InvalidOperationException("A quote needs a tenant.");
        var now = time.GetUtcNow().UtcDateTime;
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(tenant.TimeZone);
        var fees = new DeliveryFeeCalculator(tenant.Fees);
        var line = new FeeLine(request.MerchantId, request.Speed, OrderStatus.Created, request.WeightGrams);
        if (request.CustomerId is not { } customerId || request.AddressId is not { } addressId)
        {
            return new DeliveryQuote(fees.AddedFee([], line), JoinsDelivery: false);
        }

        // An open group past its deadline cannot take the order (the order would lock it and open a new one)
        var pickup = await NextPickupAsync(request.PickupPointId, now, timeZone, cancellationToken);
        var group = await FindSoonestAsync(
            customerId,
            addressId,
            g => g.CanTake(now, pickup, request.Speed == DeliverySpeed.Combine && !request.DoNotHold, timeZone),
            cancellationToken);

        return group is null
            ? new DeliveryQuote(fees.AddedFee([], line), JoinsDelivery: false)
            : new DeliveryQuote(await AddedFeeAsync(group, line, fees, cancellationToken), JoinsDelivery: true);
    }

    /// <summary>
    /// An advance is the delivery's first-shop fee and is paid once, whatever shop each order comes from: an order
    /// joining a delivery whose advance is paid waits for nothing, and one joining a delivery still waiting for its
    /// advance waits for the same payment.
    /// </summary>
    private async Task MatchAdvanceAsync(Order order, DeliveryGroup group, DateTime now, CancellationToken cancellationToken)
    {
        if (group.IsNew)
        {
            return;
        }

        var paid = await db.Payments.AnyAsync(
            payment => payment.DeliveryGroupId == group.Id &&
                payment.Purpose == PaymentPurpose.Advance &&
                payment.Status == PaymentStatus.Paid,
            cancellationToken);
        if (paid)
        {
            order.AdvancePaid(now);

            return;
        }

        var waiting = await db.Orders
            .IgnoreQueryFilters([QueryFilters.Merchant])
            .AnyAsync(
                other => other.DeliveryGroupId == group.Id &&
                    other.CustomerStep == CustomerStep.PayInAdvance &&
                    other.ConfirmedOn == null,
                cancellationToken);
        if (waiting)
        {
            order.WaitForAdvance();
        }
    }

    /// <summary>
    /// The customer may be asked whether the order's address is the same place as another delivery's on its way in the
    /// same area (<c>CombineDeliveriesHandler</c>), so the order gets the link the SMS asks with. The text decides when
    /// it is sent whether to ask.
    /// </summary>
    private async Task AskAboutAddressAsync(Order order, CancellationToken cancellationToken)
    {
        var asked = await (
            from mine in db.CustomerAddresses
            where mine.Id == order.AddressId
            from g in db.DeliveryGroups
            join other in db.CustomerAddresses on g.AddressId equals other.Id
            where g.CustomerId == order.CustomerId &&
                other.AreaId == mine.AreaId &&
                other.Id != mine.Id &&
                other.SameAsId == null &&
                (g.Status == DeliveryGroupStatus.Open || g.Status == DeliveryGroupStatus.Locked) &&
                !db.TripStops.Any(stop => stop.DeliveryGroupId == g.Id) &&
                (other.Id < mine.Id ? mine.KeptApartOn : other.KeptApartOn) == null
            select g.Id)
            .AnyAsync(cancellationToken);
        if (asked)
        {
            order.AskCustomer();
        }
    }

    /// <summary>
    /// The customer's delivery to the address that leaves soonest among those that <paramref name="canTake"/> the
    /// order; null when none can.
    /// </summary>
    private async Task<DeliveryGroup?> FindSoonestAsync(
        long customerId,
        long addressId,
        Func<DeliveryGroup, bool> canTake,
        CancellationToken cancellationToken)
    {
        var groups = await db.DeliveryGroups.Where(g =>
            g.CustomerId == customerId &&
            g.AddressId == addressId &&
            (g.Status == DeliveryGroupStatus.Open ||
                (g.Status == DeliveryGroupStatus.Locked && g.Kind != DeliveryGroupKind.Waiting)))
            .ToListAsync(cancellationToken);

        return groups
            .Where(canTake)
            .OrderBy(g => g.LocksAt)
            .ThenBy(g => g.Id)
            .FirstOrDefault();
    }

    /// <summary>
    /// Locks the customer's open group for the address when its deadline has passed and the lock job has not reached
    /// it yet, so a new group can open beside it.
    /// </summary>
    private async Task LockOverdueAsync(Order order, DateTime now, CancellationToken cancellationToken)
    {
        var open = await FindOpenAsync(order, cancellationToken);
        if (open is null || open.CanJoin(now))
        {
            return;
        }

        open.LockIfDue(now);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The lock job or a parallel order locked it first; either way it is no longer open
        }

        db.Entry(open).State = EntityState.Detached;
    }

    /// <summary>
    /// When the pickup route of the point's zone next leaves (UTC), which decides the day the parcels reach the hub.
    /// Null when the zone has no active route.
    /// </summary>
    private async Task<DateTime?> NextPickupAsync(
        long pickupPointId,
        DateTime now,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken)
    {
        var route = await (
            from point in db.PickupPoints
            join area in db.Areas on point.AreaId equals area.Id
            join run in db.PickupRoutes on area.ZoneId equals run.ZoneId
            where point.Id == pickupPointId && !run.Archived
            select run)
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        return route?.NextPickup(now, timeZone);
    }

    /// <summary>
    /// What <paramref name="line"/> adds to the fee of <paramref name="group"/> as it stands in the database.
    /// </summary>
    private async Task<decimal> AddedFeeAsync(
        DeliveryGroup group,
        FeeLine line,
        DeliveryFeeCalculator fees,
        CancellationToken cancellationToken)
    {
        // A group not saved yet is being opened by this order
        if (group.Id == 0)
        {
            return fees.AddedFee([], line, group.Kind);
        }

        // The group's other orders belong to other merchants: the fee depends on them, but nothing about them
        // leaves this method, so the merchant filter is lifted for this query (and its packages) only
        var inGroup = await db.Orders
            .IgnoreQueryFilters([QueryFilters.Merchant])
            .Where(o => o.DeliveryGroupId == group.Id)
            .Select(o => new FeeLine(o.MerchantId, o.Speed, o.Status, o.Packages.Sum(p => p.WeightGrams)))
            .ToListAsync(cancellationToken);

        return fees.AddedFee(inGroup, line, group.Kind);
    }

    private Task<DeliveryGroup?> FindOpenAsync(Order order, CancellationToken cancellationToken)
    {
        return db.DeliveryGroups.FirstOrDefaultAsync(
            g => g.CustomerId == order.CustomerId &&
                g.AddressId == order.AddressId &&
                g.Status == DeliveryGroupStatus.Open,
            cancellationToken);
    }
}
