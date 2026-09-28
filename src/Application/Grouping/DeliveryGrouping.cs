using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Domain.Grouping;
using Domain.Orders;
using Domain.Pricing;

namespace Application.Grouping;

/// <summary>
/// Puts a new order in its delivery group and saves them together. An order that waits joins the customer's
/// open group for the address, or opens one; Deliver fast and Don't hold orders get a group of their own. The
/// window (join days, time zone) is the tenant's. Two orders opening the same customer's first group at the same
/// moment are settled by the unique index on open groups: the loser joins the winner's group. The order is priced
/// for the group it lands in (<see cref="Order.AddedFee"/>), at the tenant's prices.
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
        var spec = new NewDeliveryGroup(
            order.CustomerId,
            order.AddressId,
            hubId,
            now,
            TimeZoneInfo.FindSystemTimeZoneById(tenant.TimeZone),
            tenant.GroupJoinDays);

        var fees = new DeliveryFeeCalculator(tenant.Fees);

        var group = order.WaitsForGroup
            ? await FindJoinableAsync(order, now, cancellationToken) ?? DeliveryGroup.Open(spec)
            : DeliveryGroup.OpenAlone(spec);
        order.PlaceIn(group, await AddedFeeAsync(order, group, fees, cancellationToken));
        db.Orders.Add(order);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (order.WaitsForGroup && db.Entry(group).State == EntityState.Added)
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
            order.PlaceIn(winner, await AddedFeeAsync(order, winner, fees, cancellationToken));
            db.Entry(order).DetectChanges();
            db.Entry(group).State = EntityState.Detached;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// The customer's open group for the address if the order may still join it. An open group past its deadline
    /// (the lock job has not reached it yet) is locked here first, so a new group can open beside it.
    /// </summary>
    private async Task<DeliveryGroup?> FindJoinableAsync(Order order, DateTime now, CancellationToken cancellationToken)
    {
        var open = await FindOpenAsync(order, cancellationToken);
        if (open is null || open.CanJoin(now))
        {
            return open;
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

        return null;
    }

    /// <summary>What <paramref name="order"/> adds to the fee of <paramref name="group"/> as it stands in the database.</summary>
    private async Task<decimal> AddedFeeAsync(
        Order order,
        DeliveryGroup group,
        DeliveryFeeCalculator fees,
        CancellationToken cancellationToken)
    {
        var line = new FeeLine(order.MerchantId, order.Speed, order.Status);
        // A group not saved yet is being opened by this order
        if (group.Id == 0)
        {
            return fees.AddedFee([], line);
        }

        // The group's other orders belong to other merchants: the fee depends on them, but nothing about them
        // leaves this method, so the merchant filter is lifted for this query only
        var inGroup = await db.Orders
            .IgnoreQueryFilters([QueryFilters.Merchant])
            .Where(o => o.DeliveryGroupId == group.Id)
            .Select(o => new FeeLine(o.MerchantId, o.Speed, o.Status))
            .ToListAsync(cancellationToken);

        return fees.AddedFee(inGroup, line);
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
