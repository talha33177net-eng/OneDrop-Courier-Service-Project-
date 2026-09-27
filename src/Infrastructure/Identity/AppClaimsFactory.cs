using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Application.Common;

namespace Infrastructure.Identity;

/// <summary>Puts tenant, merchant and customer ids on the cookie so every request can scope itself without a lookup.</summary>
public class AppClaimsFactory(
    UserManager<AppUser> userManager,
    RoleManager<AppRole> roleManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<AppUser, AppRole>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(ClaimTypes.GivenName, user.DisplayName));
        AddIfSet(identity, AppClaims.TenantId, user.TenantId);
        AddIfSet(identity, AppClaims.MerchantId, user.MerchantId);
        AddIfSet(identity, AppClaims.CustomerId, user.CustomerId);

        return identity;
    }

    private static void AddIfSet(ClaimsIdentity identity, string type, long? value)
    {
        if (value.HasValue)
        {
            identity.AddClaim(new Claim(type, value.Value.ToString(CultureInfo.InvariantCulture)));
        }
    }
}
