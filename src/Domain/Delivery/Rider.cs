using Domain.Common;

namespace Domain.Delivery;

/// <summary>
/// A rider working from one hub, collecting parcels from merchants and delivering them. A rider signs in with their
/// own login (<see cref="UserId"/>) to see the day's pickups and deliveries. A rider who leaves is archived, never
/// deleted: their attempts and runs stay on record.
/// </summary>
public class Rider : TenantEntity, IArchivable
{
    private Rider()
    {
    }

    /// <summary>The hub the rider works from.</summary>
    public long HubId { get; private set; }

    /// <summary>The rider's login. Null for a rider who has none yet.</summary>
    public long? UserId { get; private set; }

    public string Name { get; private set; } = "";

    /// <summary>E.164, given to recipients when the rider is on the way.</summary>
    public string Phone { get; private set; } = "";

    public bool Archived { get; private set; }

    public static Result<Rider> Create(long hubId, string? name, string? phone, long? userId)
    {
        var rider = new Rider { UserId = userId };
        var changed = rider.Change(hubId, name, phone);

        return changed.IsSuccess ? rider : changed.Error!;
    }

    public Result Change(long hubId, string? name, string? phone)
    {
        var trimmed = name.NullIfBlank();
        if (trimmed is null || trimmed.Length > 200)
        {
            return Error.Validation("rider.name", "Enter the rider's name, at most 200 characters.");
        }

        var parsed = PhoneNumber.Parse(phone);
        if (parsed.IsFailure)
        {
            return parsed.Error!;
        }

        HubId = hubId;
        Name = trimmed;
        Phone = parsed.Value.Value;

        return Result.Success();
    }

    public void LinkLogin(long userId)
    {
        UserId ??= userId;
    }

    /// <summary>The rider stops working: no new work is assigned to them.</summary>
    public void Deactivate()
    {
        Archived = true;
    }

    public void Reactivate()
    {
        Archived = false;
    }
}
