using System.Text.Json;
using Nexora.Application.Abstractions;
using Nexora.Application.Common;
using Nexora.Domain.Entities;

namespace Nexora.Application.Services;

/// <summary>
/// Adds audit entries and outbox events to the current unit of work. Nothing is saved here: the caller's
/// SaveChanges persists the business change, its audit trail and its events atomically.
/// </summary>
public sealed class AuditTrail(IApplicationDbContext db, RequestContext context, TimeProvider clock)
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Record(
        string action, string entityType, object entityId, object? data = null,
        Guid? tenantId = null, Guid? actorId = null, string? actorEmail = null)
    {
        db.AuditLogs.Add(AuditLog.Create(
            tenantId ?? context.TenantId,
            actorId ?? context.UserId,
            actorEmail ?? context.Email ?? (context.IsSystem ? "system" : null),
            action,
            entityType,
            entityId.ToString()!,
            data is null ? null : JsonSerializer.Serialize(data, JsonOptions),
            context.CorrelationId,
            context.IpAddress,
            clock.GetUtcNow()));
    }

    public void Publish(string eventType, Guid? tenantId, object payload)
    {
        var envelope = new
        {
            type = eventType,
            tenantId,
            occurredAt = clock.GetUtcNow(),
            correlationId = context.CorrelationId,
            data = payload,
        };

        db.OutboxMessages.Add(OutboxMessage.Create(tenantId, eventType, JsonSerializer.Serialize(envelope, JsonOptions), clock.GetUtcNow()));
    }
}
