using Domain.Common;
using Domain.Grouping;

namespace Domain.Delivery;

/// <summary>
/// One rider's run from their hub on one delivery day (tenant's date): the deliveries on it are its
/// <see cref="TripStop"/>s. A rider has at most one trip a day (<c>UX_Trip_Rider_DeliveryDate</c>). Deliveries are
/// added while it is <see cref="TripStatus.Planned"/>; <see cref="Start"/> is the rider taking the parcels out.
/// </summary>
public class Trip : TenantEntity
{
    private Trip()
    {
    }

    public long RiderId { get; private set; }

    /// <summary>The hub the trip leaves from: the rider's hub on the day it was planned.</summary>
    public long HubId { get; private set; }

    public DateOnly DeliveryDate { get; private set; }

    public TripStatus Status { get; private set; }

    /// <summary>When the rider took the parcels out (UTC).</summary>
    public DateTime? StartedOn { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    /// <summary>A trip that is no longer planned takes no more deliveries.</summary>
    public static Error NotPlanned => Error.Conflict("trip.notPlanned", "This trip has already left.");

    public static Trip Plan(Rider rider, DateOnly deliveryDate)
    {
        if (rider.IsNew)
        {
            throw new InvalidOperationException("Save the rider before planning a trip for them.");
        }

        return new Trip
        {
            RiderId = rider.Id,
            HubId = rider.HubId,
            DeliveryDate = deliveryDate,
            Status = TripStatus.Planned
        };
    }

    /// <summary>
    /// Puts a closed delivery waiting at this trip's hub on the trip. The planner decides which deliveries fit the
    /// bike; one from another hub is a bug, not a business "no".
    /// </summary>
    public Result<TripStop> Add(DeliveryGroup group)
    {
        if (IsNew || group.IsNew)
        {
            throw new InvalidOperationException("Save the trip and the delivery before adding a stop.");
        }

        if (group.HubId != HubId)
        {
            throw new InvalidOperationException($"Delivery {group.Number} leaves from another hub.");
        }

        if (Status != TripStatus.Planned)
        {
            return NotPlanned;
        }

        if (group.Status != DeliveryGroupStatus.Locked)
        {
            return Error.Conflict(
                "trip.stop.notLocked",
                $"Delivery {group.Number} is {group.Status}; only a closed delivery waiting at the hub goes on a trip.");
        }

        return new TripStop(this, group);
    }

    /// <summary>The rider takes the parcels out. The caller hands each ready delivery over first.</summary>
    public Result Start(DateTime now)
    {
        if (Status != TripStatus.Planned)
        {
            return NotPlanned;
        }

        Status = TripStatus.Out;
        StartedOn = now;

        return Result.Success();
    }

    /// <summary>
    /// A trip still planned when its day has passed never left; cancelling it frees its deliveries for a trip today.
    /// False when it is not planned.
    /// </summary>
    public bool CancelIfPast(DateOnly today)
    {
        if (Status != TripStatus.Planned || DeliveryDate >= today)
        {
            return false;
        }

        Status = TripStatus.Cancelled;

        return true;
    }
}
