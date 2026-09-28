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

    /// <summary>
    /// The hub the parcel is at: where it was last scanned in. Null until it first reaches a hub, and while it is on
    /// the shuttle to <see cref="ShuttleToHubId"/>.
    /// </summary>
    public long? HubId { get; private set; }

    /// <summary>When it was last scanned in at a hub (UTC). Null only until it first reaches one.</summary>
    public DateTime? ReceivedOn { get; private set; }

    /// <summary>The hub the parcel is travelling to on the hub shuttle. Null when it is not on the shuttle.</summary>
    public long? ShuttleToHubId { get; private set; }

    internal void ReceiveAt(long hubId, DateTime now)
    {
        HubId = hubId;
        ReceivedOn = now;
        ShuttleToHubId = null;
    }

    internal void LoadForShuttle(long toHubId)
    {
        HubId = null;
        ShuttleToHubId = toHubId;
    }
}
