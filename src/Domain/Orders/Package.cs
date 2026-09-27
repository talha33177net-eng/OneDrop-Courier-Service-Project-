using Domain.Common;

namespace Domain.Orders;

/// <summary>One physical parcel of an order. Carries MerchantId itself so the merchant filter needs no join.</summary>
public class Package : TenantEntity, IMerchantOwned
{
    private Package()
    {
    }

    internal Package(Order order, int sequence, string description, int weightGrams)
    {
        Order = order;
        MerchantId = order.MerchantId;
        Sequence = sequence;
        Description = description.Trim();
        WeightGrams = weightGrams;
    }

    public long OrderId { get; private set; }

    public Order? Order { get; private set; }

    public long MerchantId { get; private set; }

    /// <summary>1, 2, 3 within the order. The label reads {order number}-{sequence}.</summary>
    public int Sequence { get; private set; }

    public string Description { get; private set; } = "";

    public int WeightGrams { get; private set; }
}
