namespace Nexora.Domain.Common;

/// <summary>Base type for aggregates. Ids are UUIDv7 so they are time-ordered and index friendly.</summary>
public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.CreateVersion7();
}

/// <summary>Marks an entity that belongs to exactly one tenant. These entities get a global tenant query filter.</summary>
public interface ITenantOwned
{
    Guid TenantId { get; }
}

/// <summary>Optimistic concurrency token. Mapped to PostgreSQL's <c>xmin</c> system column.</summary>
public interface IVersioned
{
    uint Version { get; }
}
