using Domain.Common;
using Domain.Customers;

namespace Domain.Delivery;

/// <summary>
/// A rider working from one hub. <see cref="MaxParcels"/> and <see cref="MaxWeightGrams"/> are what their bike
/// carries on one trip, set per rider (bikes differ) with no default. A rider signs in with their own login
/// (<see cref="UserId"/>) to see the day's stops.
/// </summary>
public class Rider : TenantEntity, IArchivable
{
    private Rider()
    {
    }

    public Rider(long hubId, string name, PhoneNumber phone, TripLoad limit, long? userId)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit.Parcels, 1, nameof(limit));
        ArgumentOutOfRangeException.ThrowIfLessThan(limit.WeightGrams, 1, nameof(limit));

        HubId = hubId;
        Name = name.Trim();
        Phone = phone.Value;
        MaxParcels = limit.Parcels;
        MaxWeightGrams = limit.WeightGrams;
        UserId = userId;
    }

    /// <summary>The hub the rider's trips leave from.</summary>
    public long HubId { get; private set; }

    /// <summary>The rider's login. Null for a rider who has none yet.</summary>
    public long? UserId { get; private set; }

    public string Name { get; private set; } = "";

    /// <summary>E.164, like customers' phones.</summary>
    public string Phone { get; private set; } = "";

    public int MaxParcels { get; private set; }

    public int MaxWeightGrams { get; private set; }

    public bool Archived { get; private set; }

    /// <summary>What the bike carries on one trip.</summary>
    public TripLoad Limit => new(MaxParcels, MaxWeightGrams);
}
