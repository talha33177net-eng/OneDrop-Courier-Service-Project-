using Microsoft.Extensions.Configuration;
using Application.Abstractions;

namespace Infrastructure.MultiTenancy;

/// <summary>
/// Builds the customer's page addresses from <c>Links:PortalUrlFormat</c> (for example
/// <c>http://{slug}.localhost:5080/</c>), with the current tenant's slug. A link must work from an SMS, so it is never
/// taken from the request that happens to be running.
/// </summary>
public class CustomerLinks(IConfiguration configuration, ITenantContext tenantContext) : ICustomerLinks
{
    public string Order(string token)
    {
        return Portal() + "Customer/Order?token=" + Uri.EscapeDataString(token);
    }

    public string Shops()
    {
        return Portal() + "Shops";
    }

    public string Deliveries()
    {
        return Portal() + "Customer";
    }

    private string Portal()
    {
        var tenant = tenantContext.Tenant ?? throw new InvalidOperationException("A customer link needs a tenant.");
        var format = configuration["Links:PortalUrlFormat"]
            ?? throw new InvalidOperationException("Links:PortalUrlFormat is not set.");

        return format.Replace("{slug}", tenant.Slug, StringComparison.Ordinal);
    }
}
