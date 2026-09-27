using Domain.Common;

namespace Domain.Network;

/// <summary>
/// An area of the city (e.g. Mirpur) with its own pickup routes. Every zone is served by one hub, where
/// the delivery groups of customers living in the zone are built. A hub can serve several zones.
/// </summary>
public class Zone : TenantEntity, IArchivable
{
    private Zone()
    {
    }

    public Zone(string code, string name, long hubId)
    {
        Code = code.ToUpperInvariant();
        Name = name;
        HubId = hubId;
    }

    public string Code { get; private set; } = "";

    public string Name { get; private set; } = "";

    public long HubId { get; private set; }

    public Hub? Hub { get; private set; }

    public bool Archived { get; private set; }
}
