using Domain.Common;

namespace Domain.Merchants;

/// <summary>Where the pickup route collects a merchant's parcels. A merchant has one default point.</summary>
public class PickupPoint : TenantEntity, IMerchantOwned, IArchivable
{
    private PickupPoint()
    {
    }

    public PickupPoint(long merchantId, long areaId, string name, string address, string contactPhone, bool isDefault)
    {
        MerchantId = merchantId;
        AreaId = areaId;
        Name = name.Trim();
        Address = address.Trim();
        ContactPhone = contactPhone.Trim();
        IsDefault = isDefault;
    }

    public long MerchantId { get; private set; }

    public long AreaId { get; private set; }

    public string Name { get; private set; } = "";

    public string Address { get; private set; } = "";

    public string ContactPhone { get; private set; } = "";

    public bool IsDefault { get; private set; }

    public bool Archived { get; private set; }
}
