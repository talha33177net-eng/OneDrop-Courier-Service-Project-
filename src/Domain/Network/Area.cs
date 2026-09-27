using Domain.Common;

namespace Domain.Network;

/// <summary>
/// A neighbourhood picked from a list (e.g. "Mirpur 10"). Dhaka addresses are too messy to geocode in the
/// MVP, so every address names an area and the area decides the zone.
/// </summary>
public class Area : TenantEntity, IArchivable
{
    private Area()
    {
    }

    public Area(string name, long zoneId)
    {
        Name = name;
        ZoneId = zoneId;
    }

    public string Name { get; private set; } = "";

    public long ZoneId { get; private set; }

    public Zone? Zone { get; private set; }

    public bool Archived { get; private set; }
}
