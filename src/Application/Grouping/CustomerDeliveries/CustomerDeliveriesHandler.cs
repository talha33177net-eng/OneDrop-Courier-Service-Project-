using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Application.Grouping.CombineDeliveries;
using Domain.Delivery;
using Domain.Grouping;
using Domain.Orders;
using Domain.Pricing;

namespace Application.Grouping.CustomerDeliveries;

/// <summary>One of the customer's orders inside a delivery.</summary>
public sealed record CustomerDeliveryOrder(string Number, string Shop, OrderStatus Status, int Packages, decimal Cod);

/// <summary>
/// One delivery as its customer sees it: every shop in it, and the fee recalculated on what is still to be
/// delivered. <see cref="DeliveryDay"/> is in the tenant's time zone; while the delivery is open, other shops can
/// join until the end of the day before. <see cref="ShipNowFee"/> is what Ship now would add to the fee (the fast
/// difference when it brings the day forward, 0 on the last day to join); null when the delivery is not open.
/// <see cref="SameAddress"/> asks whether its address is the same place as another delivery's in the area.
/// </summary>
public sealed record CustomerDelivery(
    string Number,
    DeliveryGroupStatus Status,
    string Address,
    DateOnly DeliveryDay,
    IReadOnlyList<CustomerDeliveryOrder> Orders,
    decimal Fee,
    decimal Savings,
    decimal? ShipNowFee,
    SameAddressQuestion? SameAddress = null)
{
    public DateOnly LastDayToJoin => DeliveryDay.AddDays(-1);

    public IReadOnlyList<string> Shops => [.. ForDelivery.Select(order => order.Shop).Distinct().Order()];

    public int Packages => ForDelivery.Sum(order => order.Packages);

    /// <summary>Packages the pickup route has already taken from their shops.</summary>
    public int PackagesCollected =>
        ForDelivery.Where(order => order.Status != OrderStatus.Created).Sum(order => order.Packages);

    /// <summary>Product money for the shops, collected at the door with the fee.</summary>
    public decimal Cod => ForDelivery.Sum(order => order.Cod);

    private IEnumerable<CustomerDeliveryOrder> ForDelivery => Orders.Where(order => Order.IsForDelivery(order.Status));
}

/// <summary>
/// The customer's deliveries on their way (open, locked or out with a rider), soonest first, and the most recent
/// finished ones. <see cref="ExtraShopFee"/> is what one more shop adds to an open delivery.
/// </summary>
public sealed record CustomerDeliveries(
    IReadOnlyList<CustomerDelivery> OnTheWay,
    IReadOnlyList<CustomerDelivery> Earlier,
    decimal ExtraShopFee);

/// <summary>
/// "My deliveries" for the signed-in customer, in the tenant's time zone and at its prices. The customer sees every
/// shop in their own deliveries; merchants never see a delivery at all.
/// </summary>
public class CustomerDeliveriesHandler(
    IAppDbContext db,
    ITenantContext tenantContext,
    CombineDeliveriesHandler combine,
    TimeProvider time)
{
    public const int EarlierShown = 10;

    private static readonly DeliveryGroupStatus[] Finished = [DeliveryGroupStatus.Delivered, DeliveryGroupStatus.Cancelled];

    public async Task<CustomerDeliveries> HandleAsync(long customerId, CancellationToken cancellationToken = default)
    {
        var tenant = tenantContext.Tenant ?? throw new InvalidOperationException("Deliveries need a tenant.");
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(tenant.TimeZone);
        var fees = new DeliveryFeeCalculator(tenant.Fees);
        var now = time.GetUtcNow().UtcDateTime;

        var deliveries =
            from delivery in db.DeliveryGroups
            join address in db.CustomerAddresses on delivery.AddressId equals address.Id
            join area in db.Areas on address.AreaId equals area.Id
            where delivery.CustomerId == customerId &&

                // A delivery combined into another has no orders left: it is not shown
                db.Orders.Any(order => order.DeliveryGroupId == delivery.Id)
            select new DeliveryRow
            {
                Id = delivery.Id,
                Number = delivery.Number,
                Status = delivery.Status,
                LocksAt = delivery.LocksAt,
                Group = delivery,
                Address = address.Line1 + ", " + area.Name
            };
        var onTheWay = await deliveries
            .Where(delivery => !Finished.Contains(delivery.Status))
            .OrderBy(delivery => delivery.LocksAt)
            .ThenBy(delivery => delivery.Id)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var earlier = await deliveries
            .Where(delivery => Finished.Contains(delivery.Status))
            .OrderByDescending(delivery => delivery.LocksAt)
            .ThenByDescending(delivery => delivery.Id)
            .Take(EarlierShown)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var ids = onTheWay.Concat(earlier).Select(delivery => delivery.Id).ToList();
        var orders = (await (
            from order in db.Orders
            join merchant in db.Merchants on order.MerchantId equals merchant.Id
            where order.CustomerId == customerId && ids.Contains(order.DeliveryGroupId)
            orderby order.Id
            select new
            {
                order.DeliveryGroupId,
                Line = new FeeLine(order.MerchantId, order.Speed, order.Status, order.Packages.Sum(p => p.WeightGrams)),
                Shown = new CustomerDeliveryOrder(order.Number, merchant.Name, order.Status, order.Packages.Count, order.CodAmount)
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken))
            .ToLookup(order => order.DeliveryGroupId);

        // A delivery handed over shows the fee the rider collected (its part of the visit's one fee)
        var collected = await db.TripStops
            .Where(stop => ids.Contains(stop.DeliveryGroupId) && stop.Outcome == StopOutcome.Delivered)
            .GroupBy(stop => stop.DeliveryGroupId)
            .Select(stops => new { GroupId = stops.Key, Fee = stops.Sum(stop => stop.FeeCollected ?? 0) })
            .ToDictionaryAsync(stop => stop.GroupId, stop => stop.Fee, cancellationToken);

        var questions = await combine.QuestionsAsync(customerId, cancellationToken);

        return new CustomerDeliveries([.. onTheWay.Select(Describe)], [.. earlier.Select(Describe)], tenant.ExtraShopFee);

        CustomerDelivery Describe(DeliveryRow delivery)
        {
            var inDelivery = orders[delivery.Id].ToList();
            var lines = inDelivery.Select(order => order.Line).ToList();
            decimal? shipNowFee = !delivery.Group.CanJoin(now) ? null
                : delivery.Group.ShipNowBringsForward(now, timeZone) ? fees.ShipNowFee
                : 0;

            // Delivery day starts at LocksAt
            return new CustomerDelivery(
                delivery.Number,
                delivery.Status,
                delivery.Address,
                DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(delivery.LocksAt, timeZone)),
                [.. inDelivery.Select(order => order.Shown)],
                collected.TryGetValue(delivery.Id, out var fee) ? fee : fees.GroupFee(lines, delivery.Group.Kind),
                fees.Savings(lines, delivery.Group.Kind),
                shipNowFee,
                questions.FirstOrDefault(question => question.Delivery == delivery.Number));
        }
    }

    // Initialised by member, not by constructor, so EF can filter and sort the projection
    private sealed record DeliveryRow
    {
        public long Id { get; init; }

        public string Number { get; init; } = "";

        public DeliveryGroupStatus Status { get; init; }

        public DateTime LocksAt { get; init; }

        public DeliveryGroup Group { get; init; } = null!;

        public string Address { get; init; } = "";
    }
}
