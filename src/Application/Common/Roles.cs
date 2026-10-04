namespace Application.Common;

/// <summary>Role names. The rows are seeded by the database project's post-deployment script.</summary>
public static class Roles
{
    /// <summary>Platform (company) staff. The only role with no tenant.</summary>
    public const string PlatformAdmin = "PlatformAdmin";

    /// <summary>The courier's admins: the whole company (parcels, merchants, riders, rates, payouts).</summary>
    public const string TenantAdmin = "TenantAdmin";

    public const string Merchant = "Merchant";

    /// <summary>Staff at a hub: scanning, sorting, assigning riders, closing riders' runs.</summary>
    public const string HubStaff = "HubStaff";

    public const string Rider = "Rider";
}

/// <summary>Claim types the app adds to the signed-in principal.</summary>
public static class AppClaims
{
    public const string TenantId = "app:tenant";

    public const string MerchantId = "app:merchant";
}
