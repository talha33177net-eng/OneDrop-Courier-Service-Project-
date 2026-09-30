using Domain.Common;

namespace Domain.Orders;

/// <summary>
/// An order moved to <paramref name="Status"/> (<see cref="Order.MoveTo"/>). Its shop hears it by webhook. The status
/// is carried because the order may move on again before the message is sent.
/// </summary>
public sealed record OrderStatusChanged(Order Order, OrderStatus Status) : IDomainEvent;
