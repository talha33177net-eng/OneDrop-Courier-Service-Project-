using Domain.Common;

namespace Domain.Network;

/// <summary>
/// A branch where parcels are received, sorted, sent on to other hubs and handed to riders. Every zone is served by
/// one hub: the hub of a parcel's destination zone delivers it, the hub of its pickup point's zone collects it.
/// </summary>
public class Hub : TenantEntity, IArchivable
{
    private Hub()
    {
    }

    public Hub(string code, string name, string address, string phone)
    {
        Code = code.ToUpperInvariant();
        Name = name;
        Address = address;
        Phone = phone;
    }

    public string Code { get; private set; } = "";

    public string Name { get; private set; } = "";

    public string Address { get; private set; } = "";

    public string Phone { get; private set; } = "";

    public bool Archived { get; private set; }
}
