using Domain.Common;

namespace Domain.Network;

/// <summary>
/// A neighbourhood or thana picked from a list (e.g. "Mirpur 10", "Sylhet Sadar"). Addresses are too varied to
/// geocode, so every recipient address names an area, and the area decides the zone, the delivering hub and the
/// charge.
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
