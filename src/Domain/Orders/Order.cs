using Domain.Common;
using Domain.Grouping;

namespace Domain.Orders;

/// <summary>Everything needed to accept an order. The customer and address are already resolved.</summary>
public sealed record NewOrder(
    long MerchantId,
    long CustomerId,
    long AddressId,
    long PickupPointId,
    string RecipientName,
    decimal CodAmount,
    decimal DeclaredValue,
    DeliverySpeed Speed,
    bool DoNotHold,
    IReadOnlyList<NewPackage> Packages)
{
    public string? ExternalReference { get; init; }

    public string? IdempotencyKey { get; init; }

    public byte[]? RequestHash { get; init; }

    public string? Note { get; init; }
}

public sealed record NewPackage(string Description, int WeightGrams);

/// <summary>
/// One merchant's order for one customer. It owns its packages and its status history and only moves
/// between the statuses <see cref="Transitions"/> allows.
/// </summary>
public class Order : TenantEntity, IMerchantOwned
{
    public const int MaxPackages = 20;
    public const int MaxPackageWeightGrams = 20_000;

    private static readonly Dictionary<OrderStatus, OrderStatus[]> Transitions = new()
    {
        [OrderStatus.Created] = [OrderStatus.PickedUp, OrderStatus.Cancelled],
        [OrderStatus.PickedUp] = [OrderStatus.AtHub, OrderStatus.Cancelled],
        [OrderStatus.AtHub] = [OrderStatus.OutForDelivery, OrderStatus.Cancelled],
        [OrderStatus.OutForDelivery] = [OrderStatus.Delivered, OrderStatus.Refused, OrderStatus.AtHub],
        [OrderStatus.Refused] = [OrderStatus.ReturnedToMerchant],
        [OrderStatus.Delivered] = [],
        [OrderStatus.ReturnedToMerchant] = [],
        [OrderStatus.Cancelled] = []
    };

    private readonly List<Package> packages = [];
    private readonly List<OrderStatusHistory> history = [];

    private Order()
    {
    }

    public long MerchantId { get; private set; }

    public long CustomerId { get; private set; }

    public long AddressId { get; private set; }

    /// <summary>The delivery group this order travels in, set by <see cref="PlaceIn"/> before the first save.</summary>
    public long DeliveryGroupId { get; private set; }

    /// <summary>Set with <see cref="PlaceIn"/> so a group opened for this order is saved with it.</summary>
    public DeliveryGroup? DeliveryGroup { get; private set; }

    public long PickupPointId { get; private set; }

    /// <summary>
    /// Public order number (OD-100001), assigned by the database sequence on insert. Null until saved: a
    /// non-null value here would be sent to the database instead of letting the sequence fill it.
    /// </summary>
    public string Number { get; private set; } = null!;

    /// <summary>The merchant's own reference, e.g. their web order number.</summary>
    public string? ExternalReference { get; private set; }

    /// <summary>The Idempotency-Key header. A retried request with the same key returns this order.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>Hash of the request body, so a reused key with a different body is rejected.</summary>
    public byte[]? RequestHash { get; private set; }

    public string RecipientName { get; private set; } = "";

    public OrderStatus Status { get; private set; }

    public DeliverySpeed Speed { get; private set; }

    /// <summary>Food, medicine or dated gifts: the merchant asked us not to hold it for the group.</summary>
    public bool DoNotHold { get; private set; }

    /// <summary>Product money the rider collects for the merchant. Zero when paid online.</summary>
    public decimal CodAmount { get; private set; }

    /// <summary>Liability cap if the parcel is lost or damaged.</summary>
    public decimal DeclaredValue { get; private set; }

    /// <summary>
    /// What this order added to its group's delivery fee when it was accepted, at the tenant's prices of that
    /// moment: the base fee when it opened the group, the extra-shop fee for a new shop, 0 for a shop already in
    /// it. Kept so the merchant's response, and any replay of it, never changes. The customer pays the group's
    /// fee, recalculated at the door on what is actually delivered.
    /// </summary>
    public decimal AddedFee { get; private set; }

    public string? Note { get; private set; }

    /// <summary>
    /// When a rider left without the order because it was not ready at the hub, and it moved to a later delivery
    /// (<see cref="FollowUpIn"/>). The shop's late-handover fee is decided from it.
    /// </summary>
    public DateTime? LeftBehindOn { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyList<Package> Packages => packages;

    public IReadOnlyList<OrderStatusHistory> History => history;

    public int TotalWeightGrams => packages.Sum(package => package.WeightGrams);

    /// <summary>
    /// True when the order joins the customer's open group and waits for it. Deliver fast and Don't hold orders
    /// travel alone, the next day.
    /// </summary>
    public bool WaitsForGroup => Speed == DeliverySpeed.Combine && !DoNotHold;

    /// <summary>
    /// False once the order will not reach the customer: cancelled, refused at the door or returned to the shop.
    /// Only orders for delivery are charged a fee, collect COD and count as the delivery's packages.
    /// </summary>
    public static bool IsForDelivery(OrderStatus status)
    {
        return status is not (OrderStatus.Cancelled or OrderStatus.Refused or OrderStatus.ReturnedToMerchant);
    }

    public static Result<Order> Create(NewOrder spec)
    {
        if (spec.Packages.Count == 0)
        {
            return Error.Validation("order.packages.required", "An order needs at least one package.");
        }

        if (spec.Packages.Count > MaxPackages)
        {
            return Error.Validation("order.packages.tooMany", $"An order can have at most {MaxPackages} packages.");
        }

        if (spec.Packages.Any(package => package.WeightGrams is <= 0 or > MaxPackageWeightGrams))
        {
            return Error.Validation(
                "order.packages.weight",
                $"Each package must weigh between 1 g and {MaxPackageWeightGrams:N0} g.");
        }

        if (spec.CodAmount < 0 || spec.DeclaredValue < 0)
        {
            return Error.Validation("order.amounts.negative", "Amounts cannot be negative.");
        }

        var order = new Order
        {
            MerchantId = spec.MerchantId,
            CustomerId = spec.CustomerId,
            AddressId = spec.AddressId,
            PickupPointId = spec.PickupPointId,
            RecipientName = spec.RecipientName.Trim(),
            CodAmount = spec.CodAmount,
            DeclaredValue = spec.DeclaredValue,
            Speed = spec.Speed,
            DoNotHold = spec.DoNotHold,
            ExternalReference = spec.ExternalReference.NullIfBlank(),
            IdempotencyKey = spec.IdempotencyKey.NullIfBlank(),
            RequestHash = spec.RequestHash,
            Note = spec.Note.NullIfBlank(),
            Status = OrderStatus.Created
        };

        var sequence = 1;
        foreach (var package in spec.Packages)
        {
            order.packages.Add(new Package(order, sequence++, package.Description, package.WeightGrams));
        }

        order.history.Add(new OrderStatusHistory(order, OrderStatus.Created, "Order received"));

        return order;
    }

    /// <summary>
    /// Puts the order in a delivery group of the same customer and address. The caller decides which group
    /// (<see cref="DeliveryGroup.CanJoin"/>) and prices the move (<see cref="AddedFee"/>); a group for someone
    /// else is a bug, not a business "no". Placing it again before it is saved (the open-group race) replaces the
    /// first placement, so the customer hears about one delivery only.
    /// </summary>
    public void PlaceIn(DeliveryGroup group, decimal addedFee)
    {
        if (group.CustomerId != CustomerId || group.AddressId != AddressId)
        {
            throw new InvalidOperationException("An order can only travel in its own customer and address's group.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(addedFee);

        DeliveryGroup = group;
        DeliveryGroupId = group.Id;
        AddedFee = addedFee;
        Withdraw<OrderPlacedInDelivery>();
        Raise(new OrderPlacedInDelivery(this));
    }

    public bool CanMoveTo(OrderStatus status)
    {
        return Transitions[Status].Contains(status);
    }

    public Result MoveTo(OrderStatus status, string? note = null)
    {
        if (!CanMoveTo(status))
        {
            return Error.Conflict("order.status.transition", $"An order that is {Status} cannot become {status}.");
        }

        Status = status;
        history.Add(new OrderStatusHistory(this, status, note));

        return Result.Success();
    }

    /// <summary>
    /// The collector scanned one of the order's labels at the shop: the whole order leaves the merchant. Scanning it
    /// again, or another of its labels, changes nothing.
    /// </summary>
    public Result<ScanOutcome> Collect()
    {
        if (Status is OrderStatus.PickedUp or OrderStatus.AtHub)
        {
            return ScanOutcome.AlreadyRecorded;
        }

        if (Status != OrderStatus.Created)
        {
            return Error.Conflict("order.scan.collect", $"{Number} is {Status}, so there is nothing to collect.");
        }

        MoveTo(OrderStatus.PickedUp, "Collected from the shop");

        return ScanOutcome.Recorded;
    }

    /// <summary>
    /// Hub staff scanned package <paramref name="sequence"/> in at a hub. The order is <see cref="OrderStatus.AtHub"/>
    /// once every package has reached a hub; a parcel that arrives without a pickup scan was evidently collected.
    /// Scanning a package again at the same hub changes nothing.
    /// </summary>
    public Result<ScanOutcome> ReceiveAtHub(int sequence, long hubId, DateTime now)
    {
        var package = packages.SingleOrDefault(p => p.Sequence == sequence);
        if (package is null)
        {
            return Error.NotFound("order.scan.package", $"{Number} has no package {sequence}.");
        }

        // A refused order comes back to the hub on its way to the shop; its status stays Refused
        if (Status is not (OrderStatus.Created or OrderStatus.PickedUp or OrderStatus.AtHub or OrderStatus.Refused))
        {
            return Error.Conflict("order.scan.receive", $"{Number} is {Status}, so it cannot be received at a hub.");
        }

        if (package.HubId == hubId)
        {
            return ScanOutcome.AlreadyRecorded;
        }

        package.ReceiveAt(hubId, now);
        if (Status == OrderStatus.Created)
        {
            MoveTo(OrderStatus.PickedUp, "Reached a hub without a pickup scan");
        }

        // A package already on the shuttle between hubs has reached the hub network too
        if (Status == OrderStatus.PickedUp && packages.All(p => p.ReceivedOn is not null))
        {
            MoveTo(OrderStatus.AtHub, "Every package scanned in at a hub");
        }

        return ScanOutcome.Recorded;
    }

    /// <summary>
    /// Hub staff loaded package <paramref name="sequence"/> on the shuttle at <paramref name="hubId"/>, bound for
    /// <paramref name="toHubId"/>, the hub its delivery leaves from. The package must have been scanned in here, and a
    /// parcel whose delivery leaves from this hub belongs on its shelf. It is received at the other end with
    /// <see cref="ReceiveAtHub"/>. Loading it again changes nothing.
    /// </summary>
    public Result<ScanOutcome> LoadForShuttle(int sequence, long hubId, long toHubId)
    {
        var package = packages.SingleOrDefault(p => p.Sequence == sequence);
        if (package is null)
        {
            return Error.NotFound("order.scan.package", $"{Number} has no package {sequence}.");
        }

        if (Status is not (OrderStatus.PickedUp or OrderStatus.AtHub))
        {
            return Error.Conflict("order.scan.shuttle", $"{Number} is {Status}, so it does not travel on the shuttle.");
        }

        if (package.HubId is null && package.ShuttleToHubId == toHubId)
        {
            return ScanOutcome.AlreadyRecorded;
        }

        if (package.HubId != hubId)
        {
            return Error.Conflict(
                "order.scan.notHere",
                $"{Number}-{sequence} has not been scanned in at this hub; scan it in first.");
        }

        if (toHubId == hubId)
        {
            return Error.Conflict(
                "order.scan.shuttle.home",
                $"{Number}-{sequence} is delivered from this hub; it goes on its shelf, not the shuttle.");
        }

        package.LoadForShuttle(toHubId);

        return ScanOutcome.Recorded;
    }

    /// <summary>
    /// True when every package is at <paramref name="hubId"/> and the order can go out from there: it is
    /// <see cref="OrderStatus.AtHub"/> and none of its packages is still on the shuttle or at another hub.
    /// </summary>
    public bool IsReadyAt(long hubId)
    {
        return Status == OrderStatus.AtHub && packages.All(p => p.HubId == hubId);
    }

    /// <summary>
    /// The rider takes the order out from <paramref name="hubId"/>: its packages leave the hub and the order is out
    /// for delivery. False, and nothing changes, when it is not ready there (<see cref="IsReadyAt"/>): it stays behind.
    /// </summary>
    public bool HandToRider(long hubId)
    {
        if (!IsReadyAt(hubId))
        {
            return false;
        }

        foreach (var package in packages)
        {
            package.LeaveHub();
        }

        MoveTo(OrderStatus.OutForDelivery, "Out with the rider");

        return true;
    }

    /// <summary>
    /// The rider left without the order (not ready when its delivery went out): it moves to a later delivery of the
    /// same customer and address, the customer's open one or a follow-up. The merchant's <see cref="AddedFee"/> stays
    /// as it was given; the customer hears where the order now travels.
    /// </summary>
    public void FollowUpIn(DeliveryGroup group, DateTime now)
    {
        if (group.CustomerId != CustomerId || group.AddressId != AddressId)
        {
            throw new InvalidOperationException("An order can only travel in its own customer and address's group.");
        }

        if (!IsForDelivery(Status) || Status is OrderStatus.OutForDelivery or OrderStatus.Delivered)
        {
            throw new InvalidOperationException($"{Number} is {Status}; only an order still waiting is left behind.");
        }

        DeliveryGroup = group;
        DeliveryGroupId = group.Id;
        LeftBehindOn = now;
        history.Add(new OrderStatusHistory(this, Status, "Not ready when the rider left; goes in a later delivery"));
        Withdraw<OrderPlacedInDelivery>();
        Raise(new OrderPlacedInDelivery(this));
    }

    /// <summary>
    /// A refused order is handed back to its shop: its parcels leave the hub. Scanning it again changes nothing.
    /// </summary>
    public Result<ScanOutcome> ReturnToMerchant()
    {
        if (Status == OrderStatus.ReturnedToMerchant)
        {
            return ScanOutcome.AlreadyRecorded;
        }

        if (Status != OrderStatus.Refused)
        {
            return Error.Conflict(
                "order.scan.return",
                $"{Number} is {Status}; only an order the customer did not take goes back to the shop.");
        }

        foreach (var package in packages)
        {
            package.LeaveHub();
        }

        MoveTo(OrderStatus.ReturnedToMerchant, "Handed back to the shop");

        return ScanOutcome.Recorded;
    }
}
