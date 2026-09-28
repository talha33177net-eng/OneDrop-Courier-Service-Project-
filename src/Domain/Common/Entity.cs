namespace Domain.Common;

/// <summary>
/// Base for every persisted object. Ids are BIGINT IDENTITY in the database, so a new entity has Id 0
/// until it is saved.
/// </summary>
public abstract class Entity
{
    private readonly List<IDomainEvent> domainEvents = [];

    public long Id { get; protected set; }

    public bool IsNew => Id == 0;

    /// <summary>
    /// Events raised since the entity was last saved. The save writes them to the outbox in the same transaction
    /// and then clears them. Methods, not properties, so EF does not try to map them.
    /// </summary>
    public IReadOnlyList<IDomainEvent> GetDomainEvents()
    {
        return domainEvents;
    }

    public void ClearDomainEvents()
    {
        domainEvents.Clear();
    }

    protected void Raise(IDomainEvent domainEvent)
    {
        domainEvents.Add(domainEvent);
    }

    /// <summary>Drops the pending events of one kind, when a later change replaces what they said.</summary>
    protected void Withdraw<TEvent>()
        where TEvent : IDomainEvent
    {
        domainEvents.RemoveAll(domainEvent => domainEvent is TEvent);
    }
}

/// <summary>
/// Something that happened to an entity that other parts of the system react to after it is saved (an SMS to the
/// customer, later a merchant webhook). Events carry the entity itself: its id exists only once the save has run.
/// </summary>
public interface IDomainEvent;

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
