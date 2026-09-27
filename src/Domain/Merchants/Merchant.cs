using Domain.Common;

namespace Domain.Merchants;

/// <summary>
/// An online shop that offers our delivery at checkout. Merchants pay nothing; they hand parcels to a scheduled
/// zone pickup and are settled for cash on delivery the next day.
/// </summary>
public class Merchant : TenantEntity, IArchivable
{
    public const int DefaultReliabilityScore = 100;

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

    /// <summary>The zone whose pickup route collects from this merchant.</summary>
    public long ZoneId { get; private set; }

    public string ContactPhone { get; private set; } = "";

    public string? ContactEmail { get; private set; }

    /// <summary>Late or missed handovers lower it; merchants that are late too often lose grouping.</summary>
    public int ReliabilityScore { get; private set; } = DefaultReliabilityScore;

    public bool Archived { get; private set; }
}
