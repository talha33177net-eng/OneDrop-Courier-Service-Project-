using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Application.Abstractions;
using Application.Common;
using Domain.Merchants;
using Infrastructure.MultiTenancy;
using Infrastructure.Persistence;

namespace Web.Authentication;

/// <summary>
/// Merchant API authentication: X-Api-Key: od_{prefix}_{secret}. The key is found by its prefix across all
/// tenants - the tenant is not known yet - and the key then selects both the tenant and the merchant for the
/// rest of the request. A key used on another tenant's subdomain is refused.
/// </summary>
public class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    AppDbContext db,
    TenantContext tenantContext,
    ITenantCatalog catalog,
    TimeProvider time)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-Api-Key";

    private static readonly TimeSpan LastUsedPrecision = TimeSpan.FromMinutes(5);

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var presented))
        {
            return AuthenticateResult.NoResult();
        }

        if (!MerchantApiKey.TryParse(presented, out var prefix, out var secret))
        {
            return AuthenticateResult.Fail("The API key is malformed.");
        }

        // Deliberately across tenants: the key is what tells us the tenant
        var key = await db.MerchantApiKeys
            .IgnoreQueryFilters([AppDbContext.TenantFilter])
            .FirstOrDefaultAsync(k => k.Prefix == prefix, Context.RequestAborted);
        if (key is null || !key.Matches(secret))
        {
            return AuthenticateResult.Fail("The API key is not valid.");
        }

        var merchantActive = await db.Merchants
            .IgnoreQueryFilters([AppDbContext.TenantFilter])
            .AnyAsync(m => m.Id == key.MerchantId && m.TenantId == key.TenantId && !m.Archived, Context.RequestAborted);
        var tenant = await catalog.FindByIdAsync(key.TenantId, Context.RequestAborted);
        if (!merchantActive || tenant is null)
        {
            return AuthenticateResult.Fail("The API key's merchant is not active.");
        }

        if (tenantContext.Tenant is not null && tenantContext.Tenant.Id != tenant.Id)
        {
            return AuthenticateResult.Fail("The API key belongs to another OneDrop operator.");
        }

        tenantContext.Set(tenant);
        await TouchAsync(key);

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, $"api-key:{key.Prefix}"),
                new Claim(ClaimTypes.Role, Roles.Merchant),
                new Claim(AppClaims.TenantId, tenant.Id.ToString(CultureInfo.InvariantCulture)),
                new Claim(AppClaims.MerchantId, key.MerchantId.ToString(CultureInfo.InvariantCulture))
            ],
            SchemeName);

        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    /// <summary>Records use at most every few minutes, so a busy integration does not write on every call.</summary>
    private async Task TouchAsync(MerchantApiKey key)
    {
        var now = time.GetUtcNow().UtcDateTime;
        if (key.LastUsedOn is null || now - key.LastUsedOn.Value > LastUsedPrecision)
        {
            key.MarkUsed(now);
            await db.SaveChangesAsync(Context.RequestAborted);
        }
    }
}
