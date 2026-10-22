using Nexora.Domain.Common;

namespace Nexora.Domain.Entities;

/// <summary>
/// Append-only audit record. There are no mutators; the DbContext refuses to update or delete rows
/// and a PostgreSQL trigger blocks UPDATE/DELETE/TRUNCATE at the database level as well.
/// </summary>
public sealed class AuditLog : Entity
{
    private AuditLog() { }

    /// <summary>Null for platform-level events (e.g. a platform admin creating a tenant, failed logins).</summary>
    public Guid? TenantId { get; private set; }

    public Guid? UserId { get; private set; }
    public string? UserEmail { get; private set; }
    public string Action { get; private set; } = null!;
    public string EntityType { get; private set; } = null!;
    public string EntityId { get; private set; } = null!;

    /// <summary>JSON document describing the change (jsonb).</summary>
    public string? Data { get; private set; }

    public string? CorrelationId { get; private set; }
    public string? IpAddress { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static AuditLog Create(
        Guid? tenantId, Guid? userId, string? userEmail, string action, string entityType, string entityId,
        string? data, string? correlationId, string? ipAddress, DateTimeOffset now) => new()
    {
        TenantId = tenantId,
        UserId = userId,
        UserEmail = userEmail,
        Action = action,
        EntityType = entityType,
        EntityId = entityId,
        Data = data,
        CorrelationId = correlationId,
        IpAddress = ipAddress,
        CreatedAt = now,
    };
}
