using Domain.Common;
using Domain.Grouping;

namespace Domain.Delivery;

/// <summary>
/// A delivery on a trip. <see cref="DeliveryDate"/> is copied from the trip so the database can keep a delivery on
/// one trip a day (<c>UX_TripStop_DeliveryGroup_DeliveryDate</c>): two planners running at once cannot both take it.
/// A delivery back at the hub after a failed attempt gets a new stop on another day's trip.
/// </summary>
public class TripStop : TenantEntity
{
    private TripStop()
    {
    }

    internal TripStop(Trip trip, DeliveryGroup group)
    {
        TripId = trip.Id;
        DeliveryGroupId = group.Id;
        DeliveryDate = trip.DeliveryDate;
    }

    public long TripId { get; private set; }

    public long DeliveryGroupId { get; private set; }

    public DateOnly DeliveryDate { get; private set; }
}
