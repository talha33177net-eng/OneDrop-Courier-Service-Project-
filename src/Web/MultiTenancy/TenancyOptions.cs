namespace Web.MultiTenancy;

public class TenancyOptions
{
    /// <summary>
    /// The host tenants are subdomains of: the production domain, "localhost" in development
    /// (browsers resolve dhaka.localhost to this machine). The bare root domain is the platform portal.
    /// </summary>
    public string RootDomain { get; set; } = "localhost";

    /// <summary>Subdomains that never name a tenant.</summary>
    public string[] ReservedSubdomains { get; set; } = ["www", "api", "admin"];

    /// <summary>The tenant slug in a host name, or null for the root domain and anything unrelated.</summary>
    public string? SlugFromHost(string host)
    {
        var suffix = "." + RootDomain;
        if (!host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var subdomain = host[..^suffix.Length];

        return subdomain.Length == 0 ||
            subdomain.Contains('.') ||
            ReservedSubdomains.Contains(subdomain, StringComparer.OrdinalIgnoreCase)
            ? null
            : subdomain.ToLowerInvariant();
    }

    /// <summary>The portal address of a tenant, keeping the current scheme and port.</summary>
    public string TenantUrl(HttpRequest request, string slug)
    {
        var port = request.Host.Port.HasValue ? $":{request.Host.Port}" : "";

        return $"{request.Scheme}://{slug}.{RootDomain}{port}/";
    }

    public string PlatformUrl(HttpRequest request)
    {
        var port = request.Host.Port.HasValue ? $":{request.Host.Port}" : "";

        return $"{request.Scheme}://{RootDomain}{port}/";
    }
}
