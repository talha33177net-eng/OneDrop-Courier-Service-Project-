using Domain.Common;

namespace Domain.Orders;

/// <summary>Append-only log of every status an order has had. Created is when it happened, UpdatedId who did it.</summary>
public class OrderStatusHistory : TenantEntity, IMerchantOwned
{
    private OrderStatusHistory()
    {
    }

    internal OrderStatusHistory(Order order, OrderStatus status, string? note)
    {
        Order = order;
        MerchantId = order.MerchantId;
        Status = status;
        Note = note.NullIfBlank();
    }

    public long OrderId { get; private set; }

    public Order? Order { get; private set; }

    public long MerchantId { get; private set; }

    public OrderStatus Status { get; private set; }

    public string? Note { get; private set; }
}
