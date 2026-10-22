using Nexora.Domain.Common;

namespace Nexora.Domain.Entities;

/// <summary>
/// Transactional outbox: written in the same database transaction as the business change and delivered
/// later by a background worker with retries, so events are never lost and never published for rolled back work.
/// </summary>
public sealed class OutboxMessage : Entity
{
    public const int MaxAttempts = 8;

    private OutboxMessage() { }

    public Guid? TenantId { get; private set; }
    public string Type { get; private set; } = null!;
    public string Payload { get; private set; } = null!;
    public OutboxStatus Status { get; private set; }
    public int Attempts { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }

    public static OutboxMessage Create(Guid? tenantId, string type, string payload, DateTimeOffset now) => new()
    {
        TenantId = tenantId,
        Type = type,
        Payload = payload,
        Status = OutboxStatus.Pending,
        OccurredAt = now,
        NextAttemptAt = now,
    };

    public void MarkProcessed(DateTimeOffset now)
    {
        Status = OutboxStatus.Processed;
        ProcessedAt = now;
        LastError = null;
    }

    /// <summary>Exponential backoff with jitter: 10s, 20s, 40s ... capped at 1 hour. Gives up after <see cref="MaxAttempts"/>.</summary>
    public void MarkFailed(string error, DateTimeOffset now, double jitter)
    {
        Attempts++;
        LastError = error.Length > 2000 ? error[..2000] : error;

        if (Attempts >= MaxAttempts)
        {
            Status = OutboxStatus.Failed;
            return;
        }

        var seconds = Math.Min(10 * Math.Pow(2, Attempts - 1), 3600);
        NextAttemptAt = now.AddSeconds(seconds * (1 + Math.Clamp(jitter, 0, 1) * 0.2));
    }
}
