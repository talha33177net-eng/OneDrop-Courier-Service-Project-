using Domain.Common;

namespace Domain.Network;

/// <summary>A small local warehouse where parcels are scanned, shelved and grouped per customer.</summary>
public class Hub : TenantEntity, IArchivable
{
    private Hub()
    {
    }

    public Hub(string code, string name, string address)
    {
        Code = code.ToUpperInvariant();
        Name = name;
        Address = address;
    }

    public string Code { get; private set; } = "";

    public string Name { get; private set; } = "";

    public string Address { get; private set; } = "";

    public bool Archived { get; private set; }
}
