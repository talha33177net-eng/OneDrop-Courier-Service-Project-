using Microsoft.AspNetCore.Identity;

namespace Infrastructure.Identity;

/// <summary>
/// A login. Staff sign in with email and password; customers with a phone OTP, their phone being the user
/// name. User names are unique per tenant, so the same phone can be a customer in Dhaka and in Chattogram.
/// </summary>
public class AppUser : IdentityUser<long>
{
    /// <summary>Null only for platform staff.</summary>
    public long? TenantId { get; set; }

    /// <summary>Set for merchant users: everything they see is filtered to this merchant.</summary>
    public long? MerchantId { get; set; }

    /// <summary>Set for customers who signed in with a phone OTP.</summary>
    public long? CustomerId { get; set; }

    public string DisplayName { get; set; } = "";

    public bool Archived { get; set; }

    public DateTime Created { get; set; }
}

public class AppRole : IdentityRole<long>
{
}
