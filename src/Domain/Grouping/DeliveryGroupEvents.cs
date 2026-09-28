using Domain.Common;
using Domain.Orders;

namespace Domain.Grouping;

/// <summary>An order was placed in its delivery group: the first one opening it, or another shop joining.</summary>
public sealed record OrderPlacedInDelivery(Order Order) : IDomainEvent;

/// <summary>An open group closed, at its deadline or by Ship now. Nothing joins it any more.</summary>
public sealed record DeliveryGroupLocked(DeliveryGroup Group) : IDomainEvent;
