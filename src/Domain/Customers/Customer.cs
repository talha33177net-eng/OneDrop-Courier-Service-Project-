using Domain.Common;

namespace Domain.Customers;

/// <summary>
/// A person who receives parcels, identified by phone number inside a tenant. Customers are shared by all
/// merchants of the tenant - that is what makes grouping possible - but a merchant only ever sees the
/// recipient details on its own orders.
/// </summary>
public class Customer : TenantEntity, IArchivable
{
    private Customer()
    {
    }

    public Customer(PhoneNumber phone, string? name)
    {
        Phone = phone.Value;
        Name = name.NullIfBlank();
    }

    public string Phone { get; private set; } = "";

    public string? Name { get; private set; }

    /// <summary>Set once the customer proves they own the number with an OTP.</summary>
    public bool PhoneVerified { get; private set; }

    public bool Archived { get; private set; }

    /// <summary>Fills in a name we did not have. A name we already hold is not overwritten by a merchant.</summary>
    public void RememberName(string? name)
    {
        if (Name is null && !string.IsNullOrWhiteSpace(name))
        {
            Name = name.Trim();
        }
    }

    public void MarkPhoneVerified()
    {
        PhoneVerified = true;
    }
}
