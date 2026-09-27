using Domain.Common;

namespace Domain.Platform;

/// <summary>
/// An operator, e.g. the Dhaka operator or a partner courier in another city. The only business entity with no
/// TenantId: it is the tenant. Prices, the grouping window and the SMS sender name are per tenant.
/// </summary>
public class Tenant : AuditedEntity, IArchivable
{
    private Tenant()
    {
    }

    public Tenant(string name, string slug)
    {
        Name = name;
        Slug = slug.ToLowerInvariant();
    }

    public string Name { get; private set; } = "";

    /// <summary>Subdomain that selects this tenant: "dhaka" for dhaka.{root domain}.</summary>
    public string Slug { get; private set; } = "";

    /// <summary>IANA or Windows id. Day boundaries for the 3-day rule are counted in this zone.</summary>
    public string TimeZone { get; private set; } = "Asia/Dhaka";

    public string CurrencyCode { get; private set; } = "BDT";

    /// <summary>Sender name shown on customer SMS.</summary>
    public string SmsSenderName { get; private set; } = "OneDrop";

    /// <summary>Fee for the first shop in a delivery group.</summary>
    public decimal BaseDeliveryFee { get; private set; } = 60m;

    /// <summary>Fee for every extra distinct shop in the same delivery group.</summary>
    public decimal ExtraShopFee { get; private set; } = 25m;

    /// <summary>Next-day delivery without waiting for the group.</summary>
    public decimal FastDeliveryFee { get; private set; } = 60m;

    /// <summary>
    /// Days, counted from the first order's day, on which later orders still join the group. The default of
    /// 2 means Day 1 and Day 2 join and the group is delivered on Day 3.
    /// </summary>
    public int GroupJoinDays { get; private set; } = 2;

    public bool Archived { get; private set; }
}
