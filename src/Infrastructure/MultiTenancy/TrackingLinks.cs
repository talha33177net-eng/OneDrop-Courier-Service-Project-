using Microsoft.Extensions.Configuration;
using Application.Abstractions;

namespace Infrastructure.MultiTenancy;

/// <summary>
/// Builds the public tracking page's address from <c>Links:PortalUrlFormat</c> (for example
/// <c>http://{slug}.localhost:5080/</c>), with the current tenant's slug. A link must work from an SMS, so it is never
/// taken from the request that happens to be running.
/// </summary>
public class TrackingLinks(IConfiguration configuration, ITenantContext tenantContext) : ITrackingLinks
{
    public string Track(string trackingCode)
    {
        var tenant = tenantContext.Tenant ?? throw new InvalidOperationException("A tracking link needs a tenant.");
        var format = configuration["Links:PortalUrlFormat"]
            ?? throw new InvalidOperationException("Links:PortalUrlFormat is not set.");

        return format.Replace("{slug}", tenant.Slug, StringComparison.Ordinal) + "Track?code=" + Uri.EscapeDataString(trackingCode);
    }
}
