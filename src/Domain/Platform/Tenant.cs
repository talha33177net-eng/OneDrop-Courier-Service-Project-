using Domain.Common;

namespace Domain.Platform;

/// <summary>
/// An operator, e.g. the Dhaka operator or a partner courier in another city. The only business entity with no
/// TenantId: it is the tenant. Prices, the grouping window and the SMS sender name are per tenant, and every one
/// of them is set explicitly for each tenant: nothing here falls back to another operator's values.
/// </summary>
public class Tenant : AuditedEntity, IArchivable
{
    private Tenant()
    {
    }

    public string Name { get; private set; } = "";

    /// <summary>Subdomain that selects this tenant: "dhaka" for dhaka.{root domain}.</summary>
    public string Slug { get; private set; } = "";

    /// <summary>IANA or Windows id. Day boundaries for the 3-day rule are counted in this zone.</summary>
    public string TimeZone { get; private set; } = "";

    public string CurrencyCode { get; private set; } = "";

    /// <summary>Sender name shown on customer SMS.</summary>
    public string SmsSenderName { get; private set; } = "";

    /// <summary>Fee for the first shop in a delivery group.</summary>
    public decimal BaseDeliveryFee { get; private set; }

    /// <summary>Fee for every extra distinct shop in the same delivery group.</summary>
    public decimal ExtraShopFee { get; private set; }

    /// <summary>Next-day delivery without waiting for the group.</summary>
    public decimal FastDeliveryFee { get; private set; }

    /// <summary>
    /// Days, counted from the first order's day, on which later orders still join the group: 2 means Day 1 and
    /// Day 2 join and the group is delivered on Day 3.
    /// </summary>
    public int GroupJoinDays { get; private set; }

    /// <summary>
    /// Grams each shop's parcels may weigh in a delivery before <see cref="ExtraKgFee"/> is charged. Nullable only
    /// because the column came after the launch seed; a tenant without it is not served (see <c>TenantCatalog</c>).
    /// </summary>
    public int? WeightAllowanceGrams { get; private set; }

    /// <summary>Fee for every started kilogram a shop's parcels weigh above <see cref="WeightAllowanceGrams"/>.</summary>
    public decimal? ExtraKgFee { get; private set; }

    /// <summary>
    /// Accepted deliveries after which a customer never pays the fee in advance, even after a refusal or when a shop
    /// asks. Nullable only because the column came after the launch seed; a tenant without it is not served.
    /// </summary>
    public int? TrustedAfterDeliveries { get; private set; }

    public bool Archived { get; private set; }
}
