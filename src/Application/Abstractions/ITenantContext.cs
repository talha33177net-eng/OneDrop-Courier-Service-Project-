using Domain.Customers;
using Domain.Merchants;
using Domain.Pricing;

namespace Application.Abstractions;

/// <summary>
/// A tenant and its settings, as cached by <see cref="ITenantCatalog"/>. <see cref="ReturnCharge"/> and
/// <see cref="LateHandoverFee"/> are what a shop pays for an order that comes back and for one left behind.
/// </summary>
public sealed record TenantInfo(
    long Id,
    string Name,
    string Slug,
    string TimeZone,
    string CurrencyCode,
    string SmsSenderName,
    decimal BaseDeliveryFee,
    decimal ExtraShopFee,
    decimal FastDeliveryFee,
    int GroupJoinDays,
    int WeightAllowanceGrams,
    decimal ExtraKgFee,
    int TrustedAfterDeliveries,
    decimal ReturnCharge,
    decimal LateHandoverFee,
    int TrustedAgainAfterDeliveries,
    int DropOffAfterLateHandovers,
    int LateHandoverWindowDays)
{
    /// <summary>The tenant's prices, for <see cref="DeliveryFeeCalculator"/>.</summary>
    public FeeSchedule Fees => new(BaseDeliveryFee, ExtraShopFee, FastDeliveryFee, WeightAllowanceGrams, ExtraKgFee);

    /// <summary>When a customer pays the fee in advance, for <see cref="CustomerStanding.StepFor"/>.</summary>
    public TrustRules Trust => new(TrustedAfterDeliveries, TrustedAgainAfterDeliveries);

    /// <summary>When a shop that is often late brings its parcels to the hub itself.</summary>
    public DropOffRule DropOff => new(DropOffAfterLateHandovers, LateHandoverWindowDays);
}

/// <summary>
/// The tenant the current request or job runs for. Resolved once per request from the subdomain, the
/// signed-in user or the merchant's API key; background jobs receive it as a parameter. When no tenant is
/// set, tenant-owned queries return nothing.
/// </summary>
public interface ITenantContext
{
    TenantInfo? Tenant { get; }

    long? TenantId => Tenant?.Id;

    bool HasTenant => Tenant is not null;
}

/// <summary>Looks tenants up by subdomain or id. Results are cached; tenants change rarely.</summary>
public interface ITenantCatalog
{
    Task<TenantInfo?> FindBySlugAsync(string slug, CancellationToken cancellationToken = default);

    Task<TenantInfo?> FindByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TenantInfo>> ListAsync(CancellationToken cancellationToken = default);
}
