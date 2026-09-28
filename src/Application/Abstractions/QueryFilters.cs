namespace Application.Abstractions;

/// <summary>
/// Names of the query filters every <see cref="IAppDbContext"/> set carries. Lifting one is a decision: say why in
/// a comment where it is done.
/// </summary>
public static class QueryFilters
{
    /// <summary>Rows of the current tenant only.</summary>
    public const string Tenant = "Tenant";

    /// <summary>Merchant-owned rows of the current merchant only, when the caller is a merchant.</summary>
    public const string Merchant = "Merchant";
}
