using Application.Abstractions;

namespace Infrastructure.MultiTenancy;

/// <summary>
/// Scoped holder of the current tenant. Set once per request by the tenant resolution middleware or the
/// API key handler, and by jobs and the seeder from their parameters. Setting a different tenant on the
/// same scope is refused: one request never spans two tenants.
/// </summary>
public class TenantContext : ITenantContext
{
    public TenantInfo? Tenant { get; private set; }

    public void Set(TenantInfo tenant)
    {
        if (Tenant is not null && Tenant.Id != tenant.Id)
        {
            throw new InvalidOperationException(
                $"This scope already runs for tenant {Tenant.Slug} and cannot switch to {tenant.Slug}.");
        }

        Tenant = tenant;
    }
}
