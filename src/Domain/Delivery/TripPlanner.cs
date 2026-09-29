namespace Domain.Delivery;

/// <summary>A rider who can take more deliveries today, with what their bike already carries.</summary>
public sealed record Bike(long RiderId, TripLoad Limit, TripLoad Carried);

/// <summary>
/// A closed delivery due out today and on no trip yet. <see cref="DueOn"/> is its delivery day: earlier than today
/// for one that came back after a failed attempt or missed its day. <see cref="Area"/> keeps neighbours together.
/// </summary>
public sealed record WaitingDelivery(long GroupId, DateOnly DueOn, string Area, TripLoad Load);

public sealed record TripAssignment(long GroupId, long RiderId);

/// <summary>Which rider takes each delivery, and the deliveries no bike had room for.</summary>
public sealed record TripPlan(IReadOnlyList<TripAssignment> Assignments, IReadOnlyList<long> NoRoom);

/// <summary>
/// Puts the day's waiting deliveries on the riders' bikes without going over any bike's limit. Deliveries already
/// late go first; the rest are taken area by area so a rider's stops are neighbours. Each goes to the first rider
/// (in the order given) whose bike still has room for it; a delivery too big for any bike's remaining room waits.
/// </summary>
public static class TripPlanner
{
    public static TripPlan Plan(IReadOnlyList<Bike> bikes, IEnumerable<WaitingDelivery> deliveries)
    {
        var carried = bikes.Select(bike => bike.Carried).ToArray();
        var assignments = new List<TripAssignment>();
        var noRoom = new List<long>();

        foreach (var delivery in deliveries
            .OrderBy(delivery => delivery.DueOn)
            .ThenBy(delivery => delivery.Area, StringComparer.Ordinal)
            .ThenBy(delivery => delivery.GroupId))
        {
            var index = 0;
            while (index < bikes.Count && !(carried[index] + delivery.Load).FitsIn(bikes[index].Limit))
            {
                index++;
            }

            if (index == bikes.Count)
            {
                noRoom.Add(delivery.GroupId);

                continue;
            }

            carried[index] += delivery.Load;
            assignments.Add(new TripAssignment(delivery.GroupId, bikes[index].RiderId));
        }

        return new TripPlan(assignments, noRoom);
    }
}
