namespace Domain.Common;

/// <summary>Rows that belong to one tenant. EF applies WHERE TenantId = @current to every query.</summary>
public interface ITenantOwned
{
    long TenantId { get; }
}

/// <summary>
/// Rows that belong to one merchant inside a tenant. When the caller is a merchant (portal user or API key),
/// EF also applies WHERE MerchantId = @merchant, so Shop A never sees Shop B's parcels.
/// </summary>
public interface IMerchantOwned
{
    long MerchantId { get; }
}

/// <summary>Rows that are hidden rather than deleted.</summary>
public interface IArchivable
{
    bool Archived { get; }
}
