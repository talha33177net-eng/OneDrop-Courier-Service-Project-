using Microsoft.AspNetCore.Authorization;
using Application.Common;

namespace Web.Authentication;

public static class Policies
{
    public const string MerchantApi = nameof(MerchantApi);
    public const string MerchantPortal = nameof(MerchantPortal);
    public const string CustomerPortal = nameof(CustomerPortal);
    public const string PlatformAdmin = nameof(PlatformAdmin);

    public static void Configure(AuthorizationOptions options)
    {
        options.AddPolicy(MerchantApi, policy => policy
            .AddAuthenticationSchemes(ApiKeyAuthenticationHandler.SchemeName)
            .RequireRole(Roles.Merchant)
            .RequireClaim(AppClaims.MerchantId));
        options.AddPolicy(MerchantPortal, policy => policy
            .RequireRole(Roles.Merchant)
            .RequireClaim(AppClaims.MerchantId));
        options.AddPolicy(CustomerPortal, policy => policy
            .RequireRole(Roles.Customer)
            .RequireClaim(AppClaims.CustomerId));
        options.AddPolicy(PlatformAdmin, policy => policy.RequireRole(Roles.PlatformAdmin));
    }
}
