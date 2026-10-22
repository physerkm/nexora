using Nexora.Domain.Common;
using Nexora.Domain.Rules;

namespace Nexora.Domain.Entities;

public sealed class Transaction : Entity, ITenantOwned, IVersioned
{
    public const int MaxReasonLength = 500;

    private Transaction() { }

    public Guid TenantId { get; private set; }
    public Guid BranchId { get; private set; }
    public string Reference { get; private set; } = null!;
    public TransactionType Type { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = null!;
    public decimal Fee { get; private set; }
    public decimal NetAmount { get; private set; }
    public TransactionStatus Status { get; private set; }
    public string? Description { get; private set; }
    public string? CounterpartyName { get; private set; }
    public string? StatusReason { get; private set; }
    public string? IdempotencyKey { get; private set; }

    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? ReviewedByUserId { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }
    public Guid? CancelledByUserId { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public uint Version { get; private set; }

    public static Transaction Create(
        Guid tenantId,
        Guid branchId,
        string reference,
        TransactionType type,
        decimal amount,
        string currency,
        decimal fee,
        string? description,
        string? counterpartyName,
        Guid createdByUserId,
        string? idempotencyKey,
        DateTimeOffset now)
    {
        if (amount <= 0)
        {
            throw new DomainException("invalid_amount", "Amount must be greater than zero.");
        }

        if (decimal.Round(amount, 2) != amount)
        {
            throw new DomainException("invalid_amount", "Amount can have at most 2 decimal places.");
        }

        if (fee < 0 || fee > amount)
        {
            throw new DomainException("invalid_fee", "Fee must be between zero and the transaction amount.");
        }

        if (!SupportedCurrencies.All.Contains(currency))
        {
            throw new DomainException("unsupported_currency", $"Currency '{currency}' is not supported.");
        }

        return new Transaction
        {
            TenantId = tenantId,
            BranchId = branchId,
            Reference = reference,
            Type = type,
            Amount = amount,
            Currency = currency,
            Fee = fee,
            NetAmount = amount - fee,
            Status = TransactionStatus.Pending,
            Description = description?.Trim(),
            CounterpartyName = counterpartyName?.Trim(),
            CreatedByUserId = createdByUserId,
            IdempotencyKey = idempotencyKey,
            CreatedAt = now,
        };
    }

    public void Approve(Guid approverId, DateTimeOffset now)
    {
        TransactionStateMachine.EnsureCanTransition(Status, TransactionStatus.Approved);
        EnsureFourEyes(approverId);
        Status = TransactionStatus.Approved;
        ReviewedByUserId = approverId;
        ReviewedAt = now;
        UpdatedAt = now;
    }

    public void Reject(Guid reviewerId, string reason, DateTimeOffset now)
    {
        TransactionStateMachine.EnsureCanTransition(Status, TransactionStatus.Rejected);
        EnsureFourEyes(reviewerId);
        Status = TransactionStatus.Rejected;
        StatusReason = RequireReason(reason);
        ReviewedByUserId = reviewerId;
        ReviewedAt = now;
        UpdatedAt = now;
    }

    public void Cancel(Guid userId, string reason, DateTimeOffset now)
    {
        TransactionStateMachine.EnsureCanTransition(Status, TransactionStatus.Cancelled);
        Status = TransactionStatus.Cancelled;
        StatusReason = RequireReason(reason);
        CancelledByUserId = userId;
        CancelledAt = now;
        UpdatedAt = now;
    }

    public void Expire(DateTimeOffset now)
    {
        TransactionStateMachine.EnsureCanTransition(Status, TransactionStatus.Expired);
        Status = TransactionStatus.Expired;
        StatusReason = "Not reviewed within the allowed time window.";
        UpdatedAt = now;
    }

    /// <summary>Four-eyes principle: whoever created a transaction cannot be the one who reviews it.</summary>
    private void EnsureFourEyes(Guid reviewerId)
    {
        if (reviewerId == CreatedByUserId)
        {
            throw new DomainException("four_eyes_violation", "A transaction cannot be reviewed by the user who created it.");
        }
    }

    private static string RequireReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("reason_required", "A reason is required.");
        }

        var trimmed = reason.Trim();
        return trimmed.Length > MaxReasonLength ? trimmed[..MaxReasonLength] : trimmed;
    }
}
