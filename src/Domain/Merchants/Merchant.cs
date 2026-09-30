using Domain.Common;

namespace Domain.Merchants;

/// <summary>
/// An online shop that offers our delivery at checkout. Merchants pay nothing; they hand parcels to a scheduled
/// zone pickup and are settled for cash on delivery the next day.
/// </summary>
public class Merchant : TenantEntity, IArchivable
{
    private Merchant()
    {
    }

    public Merchant(string name, long zoneId, string contactPhone, string? contactEmail)
    {
        Name = name.Trim();
        ZoneId = zoneId;
        ContactPhone = contactPhone.Trim();
        ContactEmail = contactEmail.NullIfBlank();
    }

    public string Name { get; private set; } = "";

    /// <summary>
    /// The merchant's home zone. Parcels are collected by the pickup route of each order's pickup point's zone,
    /// which is this one unless the merchant has a pickup point elsewhere.
    /// </summary>
    public long ZoneId { get; private set; }

    public string ContactPhone { get; private set; } = "";

    public string? ContactEmail { get; private set; }

    public bool Archived { get; private set; }
}
