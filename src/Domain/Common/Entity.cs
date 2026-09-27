namespace Domain.Common;

/// <summary>
/// Base for every persisted object. Ids are BIGINT IDENTITY in the database, so a new entity has Id 0
/// until it is saved.
/// </summary>
public abstract class Entity
{
    public long Id { get; protected set; }

    public bool IsNew => Id == 0;
}

/// <summary>
/// Adds the standard housekeeping columns. They are stamped by the save interceptor in Infrastructure,
/// never by business code, so they have no public setters.
/// </summary>
public abstract class AuditedEntity : Entity
{
    public DateTime Created { get; private set; }

    public DateTime UpdatedOn { get; private set; }

    /// <summary>The user who made the last change. Null for API-key and background changes.</summary>
    public long? UpdatedId { get; private set; }
}

/// <summary>
/// Base for everything that belongs to one operator (tenant). TenantId is stamped from the current tenant
/// when the row is first saved, and the save interceptor refuses any write to another tenant's row.
/// </summary>
public abstract class TenantEntity : AuditedEntity, ITenantOwned
{
    public long TenantId { get; private set; }
}
