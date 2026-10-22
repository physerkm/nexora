using System.Text.Json;
using Nexora.Domain;

namespace Nexora.Application.DTOs;

public sealed record ReportSummaryQuery(DateTimeOffset? From, DateTimeOffset? To, Guid? BranchId);

public sealed record CurrencyTotalDto(string Currency, long Count, long ApprovedCount, decimal ApprovedVolume, decimal FeeIncome);

public sealed record StatusCountDto(TransactionStatus Status, long Count);

public sealed record BranchSummaryDto(Guid BranchId, string BranchName, long Count, long ApprovedCount);

public sealed record DailyPointDto(DateOnly Date, long Count, long ApprovedCount);

public sealed record ReportSummaryDto(
    DateTimeOffset From,
    DateTimeOffset To,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<CurrencyTotalDto> Totals,
    IReadOnlyList<StatusCountDto> StatusBreakdown,
    IReadOnlyList<BranchSummaryDto> Branches,
    IReadOnlyList<DailyPointDto> Daily);

public sealed record AuditLogDto(
    Guid Id,
    Guid? TenantId,
    Guid? UserId,
    string? UserEmail,
    string Action,
    string EntityType,
    string EntityId,
    JsonElement? Data,
    string? CorrelationId,
    string? IpAddress,
    DateTimeOffset CreatedAt);

public sealed record AuditLogQuery(
    int? Page,
    int? PageSize,
    string? EntityType,
    string? EntityId,
    string? Action,
    Guid? UserId,
    DateTimeOffset? From,
    DateTimeOffset? To);
