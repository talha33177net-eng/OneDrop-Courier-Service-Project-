using System.Text.Json.Serialization;
using Domain.Orders;

namespace Application.Orders.CreateOrder;

/// <summary>
/// POST /api/v1/orders. The merchant is the caller (from the API key); the tenant comes with it.
/// </summary>
public sealed record CreateOrderCommand
{
    /// <summary>The merchant's own order number, shown back to them in lists and webhooks.</summary>
    public string? ExternalReference { get; init; }

    public CustomerInput? Customer { get; init; }

    public AddressInput? Address { get; init; }

    /// <summary>Optional. Defaults to the merchant's default pickup point.</summary>
    public long? PickupPointId { get; init; }

    public IReadOnlyList<PackageInput> Packages { get; init; } = [];

    /// <summary>Product money to collect at the door. 0 when the customer paid online.</summary>
    public decimal CodAmount { get; init; }

    /// <summary>Liability cap. Defaults to the COD amount.</summary>
    public decimal? DeclaredValue { get; init; }

    public DeliverySpeed Speed { get; init; } = DeliverySpeed.Combine;

    /// <summary>Food, medicine or dated gifts that must not wait for the group.</summary>
    public bool DoNotHold { get; init; }

    public string? Note { get; init; }

    /// <summary>From the Idempotency-Key header, not the body.</summary>
    [JsonIgnore]
    public string? IdempotencyKey { get; init; }
}

public sealed record CustomerInput(string? Name, string? Phone);

/// <summary>Give either AreaId or the Area name, from GET /api/v1/areas.</summary>
public sealed record AddressInput(long? AreaId, string? Area, string? Line1, string? Line2, string? Landmark);

public sealed record PackageInput(string? Description, int WeightGrams);

/// <param name="Fee">
/// What this order adds to the customer's delivery fee (<c>Order.AddedFee</c>). Never the group's total, which
/// would tell the merchant how many other shops the customer bought from.
/// </param>
public sealed record CreateOrderResult(
    long OrderId,
    string Number,
    string? ExternalReference,
    OrderStatus Status,
    DeliverySpeed Speed,
    long CustomerId,
    string Area,
    string Zone,
    string Hub,
    int PackageCount,
    decimal CodAmount,
    decimal Fee,
    DateTime Created)
{
    /// <summary>True when this is an earlier order returned for a repeated Idempotency-Key.</summary>
    public bool Replayed { get; init; }
}
