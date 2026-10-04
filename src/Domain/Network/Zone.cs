using Domain.Common;

namespace Domain.Network;

/// <summary>
/// A part of the coverage map served by one hub: a city area (Mirpur), a suburb of a city (Savar) or a district
/// (Sylhet). <see cref="City"/> and <see cref="IsSuburb"/> decide the service area a parcel is charged at
/// (<see cref="Pricing.ServiceAreas.Between"/>). A hub can serve several zones.
/// </summary>
public class Zone : TenantEntity, IArchivable
{
    private Zone()
    {
    }

    public Zone(string code, string name, long hubId, string city, bool isSuburb)
    {
        Code = code.ToUpperInvariant();
        Name = name;
        HubId = hubId;
        City = city.Trim();
        IsSuburb = isSuburb;
    }

    public string Code { get; private set; } = "";

    public string Name { get; private set; } = "";

    public long HubId { get; private set; }

    public Hub? Hub { get; private set; }

    /// <summary>The city or district the zone belongs to, e.g. "Dhaka" for both Mirpur and Savar.</summary>
    public string City { get; private set; } = "";

    /// <summary>A suburb of <see cref="City"/> (Savar, Gazipur), charged between the city and outside-city rates.</summary>
    public bool IsSuburb { get; private set; }

    public bool Archived { get; private set; }
}
