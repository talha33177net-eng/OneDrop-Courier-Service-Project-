using Domain.Orders;

namespace Application.Pricing.GetQuote;

/// <summary>
/// GET /api/v1/quote. The customer and address as the checkout has them, before an order exists. The merchant is
/// the caller (from the API key).
/// </summary>
public sealed record GetQuoteQuery
{
    public string? Phone { get; init; }

    /// <summary>Give either AreaId or the Area name, from GET /api/v1/areas.</summary>
    public long? AreaId { get; init; }

    public string? Area { get; init; }

    public string? Line1 { get; init; }

    public string? Line2 { get; init; }

    public DeliverySpeed Speed { get; init; } = DeliverySpeed.Combine;

    public bool DoNotHold { get; init; }
}

/// <param name="Fee">What the order would add to the customer's delivery fee: the same amount Create Order returns.</param>
/// <param name="JoinsDelivery">
/// True when the order would join the customer's delivery already on its way, so the checkout can show "+৳25".
/// Nothing is said about which shops or how many are in it.
/// </param>
public sealed record QuoteResult(decimal Fee, string Currency, bool JoinsDelivery);
