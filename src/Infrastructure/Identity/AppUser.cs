using Microsoft.AspNetCore.Identity;

namespace Infrastructure.Identity;

/// <summary>
/// A login: email and password, for every role. User names are unique per tenant, so the same email can sign in at two
/// couriers.
/// </summary>
public class AppUser : IdentityUser<long>
{
    /// <summary>Null only for platform staff.</summary>
    public long? TenantId { get; set; }

    /// <summary>Set for merchant users: everything they see is filtered to this merchant.</summary>
    public long? MerchantId { get; set; }

    public string DisplayName { get; set; } = "";

    public bool Archived { get; set; }

    public DateTime Created { get; set; }
}

public class AppRole : IdentityRole<long>
{
}
