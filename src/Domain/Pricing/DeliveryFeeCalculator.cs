using Domain.Orders;

namespace Domain.Pricing;

/// <summary>A tenant's delivery prices. Every amount is the tenant's own setting; none has a default.</summary>
public sealed record FeeSchedule(decimal BaseDeliveryFee, decimal ExtraShopFee, decimal FastDeliveryFee);

/// <summary>One order in a delivery group, as pricing sees it.</summary>
public sealed record FeeLine(long MerchantId, DeliverySpeed Speed, OrderStatus Status);

/// <summary>
/// The one place the delivery fee is worked out, with the tenant's prices. A group costs the base fee for its
/// first shop and the extra-shop fee for every other distinct shop; several orders from one shop count once.
/// Only orders still on their way or accepted count: a cancelled, refused or returned order's shop is not
/// charged, so the fee always follows what is actually delivered. A Deliver fast group (always a single order)
/// costs the fast fee. A Don't hold order travels alone but was the merchant's choice, so it costs the base fee.
/// </summary>
public class DeliveryFeeCalculator(FeeSchedule schedule)
{
    /// <summary>What the customer pays at the door for the group's <paramref name="orders"/>.</summary>
    public decimal GroupFee(IEnumerable<FeeLine> orders)
    {
        var charged = orders.Where(order => Order.IsForDelivery(order.Status)).ToList();
        var shops = charged.Select(order => order.MerchantId).Distinct().Count();
        if (shops == 0)
        {
            return 0;
        }

        if (charged.Any(order => order.Speed == DeliverySpeed.Fast))
        {
            return schedule.FastDeliveryFee;
        }

        return schedule.BaseDeliveryFee + schedule.ExtraShopFee * (shops - 1);
    }

    /// <summary>
    /// What the customer saves against each shop sending its orders separately, each at the tenant's base fee
    /// (a one-shop delivery). Nothing for a single shop, whatever its speed.
    /// </summary>
    public decimal Savings(IReadOnlyCollection<FeeLine> orders)
    {
        var shops = orders
            .Where(order => Order.IsForDelivery(order.Status))
            .Select(order => order.MerchantId)
            .Distinct()
            .Count();
        if (shops < 2)
        {
            return 0;
        }

        return Math.Max(0, schedule.BaseDeliveryFee * shops - GroupFee(orders));
    }

    /// <summary>
    /// What <paramref name="order"/> adds to the fee of a group already holding <paramref name="group"/>: the
    /// base fee when it opens the group, the extra-shop fee for a new shop, nothing for a shop already in it.
    /// </summary>
    public decimal AddedFee(IReadOnlyCollection<FeeLine> group, FeeLine order)
    {
        return GroupFee([.. group, order]) - GroupFee(group);
    }
}
