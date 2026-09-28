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
}
