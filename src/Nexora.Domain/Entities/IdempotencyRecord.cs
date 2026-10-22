using Nexora.Domain.Common;

namespace Nexora.Domain.Entities;

/// <summary>Remembers the outcome of a request sent with an <c>Idempotency-Key</c> header so retries are replayed, not re-executed.</summary>
public sealed class IdempotencyRecord : Entity
{
    private IdempotencyRecord() { }

    public Guid? TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public string Key { get; private set; } = null!;
    public string RequestMethod { get; private set; } = null!;
    public string RequestPath { get; private set; } = null!;
    public string RequestHash { get; private set; } = null!;
    public IdempotencyStatus Status { get; private set; }
    public int? ResponseStatusCode { get; private set; }
    public string? ResponseBody { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
}
