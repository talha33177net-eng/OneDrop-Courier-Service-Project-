using Domain.Grouping;
using Domain.Orders;

namespace Domain.Pricing;

/// <summary>
/// A tenant's delivery prices. Every amount is the tenant's own setting; none has a default.
/// <see cref="WeightAllowanceGrams"/> is the weight each shop's parcels may have in a delivery before
/// <see cref="ExtraKgFee"/> is charged for every started kilogram above it.
/// </summary>
public sealed record FeeSchedule(
    decimal BaseDeliveryFee,
    decimal ExtraShopFee,
    decimal FastDeliveryFee,
    int WeightAllowanceGrams,
    decimal ExtraKgFee);

/// <summary>One order in a delivery group, as pricing sees it. <see cref="WeightGrams"/> is all its packages.</summary>
public sealed record FeeLine(long MerchantId, DeliverySpeed Speed, OrderStatus Status, int WeightGrams)
{
    /// <summary>An order loaded with its packages.</summary>
    public static FeeLine Of(Order order)
    {
        return new FeeLine(order.MerchantId, order.Speed, order.Status, order.TotalWeightGrams);
    }
}

/// <summary>One delivery at a visit, as pricing sees it: its kind and its orders.</summary>
public sealed record FeeDelivery(DeliveryGroupKind Kind, IReadOnlyList<FeeLine> Lines);

/// <summary>
/// The one place the delivery fee is worked out, with the tenant's prices. A delivery costs the base fee for its
/// first shop and the extra-shop fee for every other distinct shop; several orders from one shop count once. The
/// first shop costs the fast fee instead when the delivery carries a Deliver fast order, the base fee plus
/// <see cref="ShipNowFee"/> when the customer brought it forward with Ship now (<see cref="DeliveryGroupKind"/>
/// ShippedNow), and only the extra-shop fee in a follow-up delivery of orders a rider left behind. Each shop's
/// parcels travel free up to the weight allowance; every started kilogram above it costs the extra-kg fee. Only
/// orders still on their way or accepted count: a cancelled, refused or returned order is not charged, so the fee
/// always follows what is actually delivered. A Don't hold order was the merchant's choice, so it costs the base fee.
/// </summary>
public class DeliveryFeeCalculator(FeeSchedule schedule)
{
    /// <summary>What the customer pays at the door for the delivery's <paramref name="orders"/>.</summary>
    public decimal GroupFee(IEnumerable<FeeLine> orders, DeliveryGroupKind kind = DeliveryGroupKind.Waiting)
    {
        var charged = orders.Where(order => Order.IsForDelivery(order.Status)).ToList();

        return ShopsFee(charged, kind) + WeightFee(charged);
    }

    /// <summary>
    /// What the customer saves against each shop sending its orders separately, each at the tenant's base fee
    /// (a one-shop delivery) and each paying for its own weight. Nothing for a single shop, whatever its speed.
    /// </summary>
    public decimal Savings(IReadOnlyCollection<FeeLine> orders, DeliveryGroupKind kind = DeliveryGroupKind.Waiting)
    {
        var charged = orders.Where(order => Order.IsForDelivery(order.Status)).ToList();
        var shops = charged.Select(order => order.MerchantId).Distinct().Count();
        if (shops < 2)
        {
            return 0;
        }

        return Math.Max(0, schedule.BaseDeliveryFee * shops - ShopsFee(charged, kind));
    }

    /// <summary>
    /// What <paramref name="order"/> adds to the fee of a delivery already holding <paramref name="group"/>: the
    /// base (or fast) fee when it opens the delivery, the extra-shop fee for a new shop, nothing for a shop already in
    /// it; plus the fast difference when it makes the delivery fast, and any kilograms it takes past its shop's
    /// allowance.
    /// </summary>
    public decimal AddedFee(
        IReadOnlyCollection<FeeLine> group,
        FeeLine order,
        DeliveryGroupKind kind = DeliveryGroupKind.Waiting)
    {
        return GroupFee([.. group, order], kind) - GroupFee(group, kind);
    }

    /// <summary>What Ship now adds when it brings a delivery forward: the fast fee in place of the base fee.</summary>
    public decimal ShipNowFee => Math.Max(0, schedule.FastDeliveryFee - schedule.BaseDeliveryFee);

    /// <summary>
    /// One visit to a customer covering several of their deliveries (same phone, same area, one trip) is one delivery
    /// to pay for: one fee over every shop handed over. It is split over the <paramref name="deliveries"/>, returned in
    /// their order, so each stop records its part and the parts add up to the visit's fee. The visit is priced as the
    /// delivery that sets the highest first-shop fee (shipped now, then waiting or next day, then follow-up); each
    /// further delivery adds what its shops and kilograms add.
    /// </summary>
    public IReadOnlyList<decimal> VisitFees(IReadOnlyList<FeeDelivery> deliveries)
    {
        var ordered = deliveries
            .Select((delivery, index) => (delivery.Kind, delivery.Lines, Index: index))
            .OrderBy(delivery => delivery.Kind switch
            {
                DeliveryGroupKind.ShippedNow => 0,
                DeliveryGroupKind.FollowUp => 2,
                _ => 1
            })
            .ThenBy(delivery => delivery.Index)
            .ToList();
        var shares = new decimal[deliveries.Count];
        var soFar = new List<FeeLine>();
        var before = 0m;
        foreach (var delivery in ordered)
        {
            soFar.AddRange(delivery.Lines);
            var total = GroupFee(soFar, ordered[0].Kind);
            shares[delivery.Index] = total - before;
            before = total;
        }

        return shares;
    }

    private decimal ShopsFee(IReadOnlyCollection<FeeLine> charged, DeliveryGroupKind kind)
    {
        var shops = charged.Select(order => order.MerchantId).Distinct().Count();
        if (shops == 0)
        {
            return 0;
        }

        var first = kind == DeliveryGroupKind.FollowUp ? schedule.ExtraShopFee
            : charged.Any(order => order.Speed == DeliverySpeed.Fast) ? schedule.FastDeliveryFee
            : kind == DeliveryGroupKind.ShippedNow ? schedule.BaseDeliveryFee + ShipNowFee
            : schedule.BaseDeliveryFee;

        return first + schedule.ExtraShopFee * (shops - 1);
    }

    private decimal WeightFee(IEnumerable<FeeLine> charged)
    {
        var startedKgs = charged
            .GroupBy(order => order.MerchantId)
            .Select(shop => Math.Max(0, shop.Sum(order => order.WeightGrams) - schedule.WeightAllowanceGrams))
            .Sum(over => (over + 999) / 1000);

        return startedKgs * schedule.ExtraKgFee;
    }
}
