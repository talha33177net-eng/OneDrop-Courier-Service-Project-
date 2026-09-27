namespace Application.Common;

/// <summary>Role names. The rows are seeded by the database project's post-deployment script.</summary>
public static class Roles
{
    /// <summary>Platform (company) staff. The only role with no tenant.</summary>
    public const string PlatformAdmin = "PlatformAdmin";

    public const string TenantAdmin = "TenantAdmin";

    public const string Merchant = "Merchant";

    public const string HubStaff = "HubStaff";

    public const string Rider = "Rider";

    public const string Customer = "Customer";
}

/// <summary>Claim types the app adds to the signed-in principal.</summary>
public static class AppClaims
{
    public const string TenantId = "app:tenant";

    public const string MerchantId = "app:merchant";

    public const string CustomerId = "app:customer";
}
