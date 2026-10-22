using Nexora.Domain;

namespace Nexora.Application.DTOs;

public sealed record TransactionDto(
    Guid Id,
    Guid TenantId,
    Guid BranchId,
    string BranchName,
    string Reference,
    TransactionType Type,
    decimal Amount,
    string Currency,
    decimal Fee,
    decimal NetAmount,
    TransactionStatus Status,
    string? Description,
    string? CounterpartyName,
    string? StatusReason,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAt,
    Guid? ReviewedByUserId,
    DateTimeOffset? ReviewedAt,
    Guid? CancelledByUserId,
    DateTimeOffset? CancelledAt,
    uint Version,
    IReadOnlyList<TransactionStatus> AllowedTransitions);

public sealed record CreateTransactionRequest(
    Guid BranchId,
    TransactionType Type,
    decimal Amount,
    string Currency,
    string? Description,
    string? CounterpartyName);

public sealed record ApproveTransactionRequest(uint Version);

public sealed record TransactionReasonRequest(uint Version, string Reason);

/// <summary>
/// Query string for GET /transactions. Supply <see cref="Cursor"/> (or <c>paging=cursor</c>) to use keyset
/// pagination, which stays fast on very large tables and skips the COUNT(*) query.
/// </summary>
public sealed class TransactionListQuery
{
    public int? Page { get; set; }
    public int? PageSize { get; set; }
    public string? SortBy { get; set; }
    public string? SortDir { get; set; }
    public TransactionStatus? Status { get; set; }
    public TransactionType? Type { get; set; }
    public Guid? BranchId { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
    public decimal? MinAmount { get; set; }
    public decimal? MaxAmount { get; set; }
    public string? Search { get; set; }
    public string? Paging { get; set; }
    public string? Cursor { get; set; }

    public bool UseCursor => Cursor is not null || string.Equals(Paging, "cursor", StringComparison.OrdinalIgnoreCase);
}
