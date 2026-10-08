using Domain.Common;

namespace Domain.Network;

/// <summary>
/// A neighbourhood or thana picked from a list (e.g. "Mirpur 10", "Sylhet Sadar"). Addresses are too varied to
/// geocode, so every recipient address names an area, and the area decides the zone, the delivering hub and the
/// charge. An area the courier stops delivering to is archived, never deleted: the parcels sent there keep it.
/// </summary>
public class Area : TenantEntity, IArchivable
{
    private Area()
    {
    }

    public string Name { get; private set; } = "";

    public long ZoneId { get; private set; }

    public Zone? Zone { get; private set; }

    public bool Archived { get; private set; }

    public static Result<Area> Create(string? name, long zoneId)
    {
        var area = new Area();
        var changed = area.Change(name, zoneId);

        return changed.IsSuccess ? area : changed.Error!;
    }

    public Result Change(string? name, long zoneId)
    {
        var trimmed = name.NullIfBlank();
        if (trimmed is null || trimmed.Length > 200)
        {
            return Error.Validation("area.name", "Name the area, such as \"Mirpur 10\", at most 200 characters.");
        }

        Name = trimmed;
        ZoneId = zoneId;

        return Result.Success();
    }

    /// <summary>The courier stops delivering to the area: it leaves the address lists, its parcels keep it.</summary>
    public void Archive()
    {
        Archived = true;
    }

    public void Restore()
    {
        Archived = false;
    }
}
