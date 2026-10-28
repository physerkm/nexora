using Nexora.Application.Common;
using Nexora.Application.DTOs;
using Nexora.Domain.Entities;

namespace Nexora.Application.Abstractions;

public sealed record AccessToken(string Token, string Jti, DateTimeOffset ExpiresAt);

public interface ITokenService
{
    AccessToken CreateAccessToken(AppUser user, IReadOnlyCollection<string> roles);
}

/// <summary>Denylist for access tokens that were revoked before they expired (logout).</summary>
public interface ITokenRevocationStore
{
    Task RevokeAsync(string jti, DateTimeOffset expiresAt, CancellationToken ct = default);
    Task<bool> IsRevokedAsync(string jti, CancellationToken ct = default);
}

/// <summary>Resource based authorization checks that the application layer needs (implemented with ASP.NET Core policies).</summary>
public interface IAccessPolicy
{
    Task EnsureBranchAccessAsync(Guid branchId, CancellationToken ct = default);
}

/// <summary>Optimised, provider specific transaction reads for large tables.</summary>
public interface ITransactionReadRepository
{
    Task<PagedResult<TransactionDto>> ListAsync(TransactionListQuery query, CancellationToken ct = default);
    Task<TransactionDto?> GetAsync(Guid id, CancellationToken ct = default);
}

public interface IReportRepository
{
    Task<ReportSummaryDto> GetSummaryAsync(Guid? tenantId, DateTimeOffset from, DateTimeOffset to, Guid? branchId, CancellationToken ct = default);
}

public enum IdempotencyOutcome
{
    Reserved,
    Replay,
    InProgress,
    PayloadMismatch
}

public sealed record IdempotencyReservation(IdempotencyOutcome Outcome, Guid RecordId, int? StatusCode = null, string? Body = null);

public interface IIdempotencyStore
{
    Task<IdempotencyReservation> TryReserveAsync(
        Guid? tenantId, Guid userId, string key, string method, string path, string requestHash, TimeSpan ttl, CancellationToken ct = default);

    Task CompleteAsync(Guid recordId, int statusCode, string? body, CancellationToken ct = default);

    Task ReleaseAsync(Guid recordId, CancellationToken ct = default);
}

public interface IIntegrationEventPublisher
{
    Task PublishAsync(OutboxMessage message, CancellationToken ct);
}

public sealed record EmailMessage(string To, string Subject, string Body);

/// <summary>
/// Queues an email for a background sender, so a request's response time does not depend on the mail server
/// (and, for "forgot password", does not reveal whether the address belongs to an account).
/// </summary>
public interface IEmailQueue
{
    void Enqueue(EmailMessage message);
}

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct);
}
