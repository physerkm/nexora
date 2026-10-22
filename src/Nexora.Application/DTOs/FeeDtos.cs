using Nexora.Domain;

namespace Nexora.Application.DTOs;

public sealed record FeeRuleDto(
    Guid Id,
    Guid TenantId,
    TransactionType TransactionType,
    string? Currency,
    decimal Percentage,
    decimal FixedAmount,
    decimal MinFee,
    decimal? MaxFee,
    bool IsActive,
    DateTimeOffset CreatedAt,
    uint Version);

public sealed record CreateFeeRuleRequest(
    TransactionType TransactionType,
    string? Currency,
    decimal Percentage,
    decimal FixedAmount,
    decimal MinFee,
    decimal? MaxFee);

public sealed record UpdateFeeRuleRequest(
    decimal Percentage,
    decimal FixedAmount,
    decimal MinFee,
    decimal? MaxFee,
    bool IsActive,
    uint Version);

public sealed record FeeQuoteRequest(TransactionType Type, decimal Amount, string Currency);

public sealed record FeeQuoteDto(decimal Amount, decimal Fee, decimal NetAmount, string Currency, Guid? FeeRuleId);
