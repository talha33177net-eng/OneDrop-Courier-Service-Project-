namespace Domain.Orders;

/// <summary>Where a parcel of a delivery due out is, when it is not on its delivery's shelf.</summary>
public enum ParcelPlace
{
    /// <summary>Still at the shop: not collected yet.</summary>
    AtTheShop,

    /// <summary>At the shop, held until the customer pays the fee in advance.</summary>
    WaitingForAdvance,

    /// <summary>Collected at the shop, not scanned in at a hub yet.</summary>
    WithTheCollector,

    /// <summary>Scanned in at another hub and not loaded on the shuttle yet.</summary>
    AtAnotherHub,

    /// <summary>On the shuttle to the delivery's hub.</summary>
    OnTheShuttle,

    /// <summary>Taken out by a rider and not scanned back in (nobody was home).</summary>
    WithARider
}

/// <summary>What the hub knows about one parcel: its order's state and where the package was last scanned.</summary>
public sealed record ParcelState(OrderStatus Status, bool WaitsForAdvance, long? HubId, long? ShuttleToHubId)
{
    /// <summary>Where the parcel is, or null when it is at <paramref name="deliveryHubId"/>, where its delivery leaves from.</summary>
    public ParcelPlace? PlaceFor(long deliveryHubId)
    {
        if (HubId == deliveryHubId)
        {
            return null;
        }

        if (ShuttleToHubId is not null)
        {
            return ParcelPlace.OnTheShuttle;
        }

        if (HubId is not null)
        {
            return ParcelPlace.AtAnotherHub;
        }

        return Status switch
        {
            OrderStatus.Created => WaitsForAdvance ? ParcelPlace.WaitingForAdvance : ParcelPlace.AtTheShop,
            OrderStatus.PickedUp => ParcelPlace.WithTheCollector,
            _ => ParcelPlace.WithARider
        };
    }
}
