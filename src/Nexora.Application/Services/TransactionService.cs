using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Nexora.Application.Abstractions;
using Nexora.Application.Common;
using Nexora.Application.DTOs;
using Nexora.Domain;
using Nexora.Domain.Common;
using Nexora.Domain.Entities;
using Nexora.Domain.Rules;

namespace Nexora.Application.Services;

public sealed record CreateTransactionResult(TransactionDto Transaction, bool Replayed);

public sealed class TransactionService(
    IApplicationDbContext db,
    ITransactionReadRepository reads,
    IAccessPolicy access,
    FeeRuleService fees,
    AuditTrail audit,
    RequestContext context,
    HybridCache cache,
    TimeProvider clock,
    ILogger<TransactionService> logger)
{
    public const string IdempotencyIndexName = "ux_transactions_idempotency_key";

    public Task<PagedResult<TransactionDto>> ListAsync(TransactionListQuery query, CancellationToken ct) => reads.ListAsync(query, ct);

    public async Task<TransactionDto> GetAsync(Guid id, CancellationToken ct) =>
        await reads.GetAsync(id, ct) ?? throw new NotFoundException("Transaction", id);

    /// <summary>
    /// Creates a pending transaction. Idempotency is enforced twice: the API filter replays stored responses,
    /// and the (tenant, user, idempotency_key) unique index guarantees a single row even if two identical requests race
    /// or the stored response was lost.
    /// </summary>
    public async Task<CreateTransactionResult> CreateAsync(CreateTransactionRequest request, string? idempotencyKey, CancellationToken ct)
    {
        var tenantId = context.RequireTenantId();
        var userId = context.RequireUserId();

        if (idempotencyKey is not null && await FindByIdempotencyKeyAsync(userId, idempotencyKey, ct) is { } existing)
        {
            return new CreateTransactionResult(existing, Replayed: true);
        }

        await access.EnsureBranchAccessAsync(request.BranchId, ct);

        var branch = await db.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == request.BranchId, ct)
                     ?? throw new BadRequestException("invalid_branch", "The branch does not exist in this tenant.");
        if (!branch.IsActive)
        {
            throw new DomainException("branch_inactive", "Transactions cannot be created for an inactive branch.");
        }

        var rule = await fees.FindRuleAsync(request.Type, request.Currency, ct);
        var fee = FeeCalculator.Calculate(request.Amount, rule);
        var now = clock.GetUtcNow();

        var transaction = Transaction.Create(
            tenantId, branch.Id, NewReference(now), request.Type, request.Amount, request.Currency, fee,
            request.Description, request.CounterpartyName, userId, idempotencyKey, now);

        db.Transactions.Add(transaction);
        audit.Record("transaction.created", "Transaction", transaction.Id, new
        {
            transaction.Reference,
            transaction.Type,
            transaction.Amount,
            transaction.Currency,
            transaction.Fee,
            feeRuleId = rule?.Id,
            transaction.BranchId,
        });
        audit.Publish("transaction.created", tenantId, Payload(transaction));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (UniqueConstraintException ex) when (ex.ConstraintName == IdempotencyIndexName && idempotencyKey is not null)
        {
            logger.LogInformation("Concurrent duplicate create detected for idempotency key {IdempotencyKey}", idempotencyKey);
            var winner = await FindByIdempotencyKeyAsync(userId, idempotencyKey, ct)
                         ?? throw new ConflictException("idempotency_conflict", "A request with this Idempotency-Key is being processed.");
            return new CreateTransactionResult(winner, Replayed: true);
        }

        await InvalidateReportsAsync(tenantId, ct);
        return new CreateTransactionResult(transaction.ToDto(branch.Name), Replayed: false);
    }

    public Task<TransactionDto> ApproveAsync(Guid id, ApproveTransactionRequest request, CancellationToken ct) =>
        TransitionAsync(id, request.Version, "transaction.approved", (t, userId, now) => t.Approve(userId, now), null, ct);

    public Task<TransactionDto> RejectAsync(Guid id, TransactionReasonRequest request, CancellationToken ct) =>
        TransitionAsync(id, request.Version, "transaction.rejected", (t, userId, now) => t.Reject(userId, request.Reason, now), request.Reason, ct);

    /// <summary>
    /// Operators may cancel only their own pending transactions; cancelling someone else's transaction
    /// or reversing an approved one requires an approver role.
    /// </summary>
    public Task<TransactionDto> CancelAsync(Guid id, TransactionReasonRequest request, CancellationToken ct) =>
        TransitionAsync(id, request.Version, "transaction.cancelled", (t, userId, now) =>
        {
            var isApprover = context.IsInRole(Roles.Manager) || context.IsInRole(Roles.TenantAdmin) || context.IsInRole(Roles.PlatformAdmin);
            if (!isApprover && (t.Status != TransactionStatus.Pending || t.CreatedByUserId != userId))
            {
                throw new ForbiddenException("Only the creator can cancel a pending transaction; approved transactions can only be cancelled by a manager.");
            }

            t.Cancel(userId, request.Reason, now);
        }, request.Reason, ct);

    public async Task<IReadOnlyList<AuditLogDto>> GetHistoryAsync(Guid id, CancellationToken ct)
    {
        if (!await db.Transactions.AnyAsync(t => t.Id == id, ct))
        {
            throw new NotFoundException("Transaction", id);
        }

        var key = id.ToString();
        var entries = await db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == "Transaction" && a.EntityId == key)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(ct);
        return entries.Select(a => a.ToDto()).ToList();
    }

    /// <summary>Background job: expires pending transactions that nobody reviewed in time. Runs in system scope.</summary>
    public async Task<int> ExpireStaleAsync(TimeSpan maxPendingAge, int batchSize, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var cutoff = now - maxPendingAge;

        var stale = await db.Transactions
            .Where(t => t.Status == TransactionStatus.Pending && t.CreatedAt < cutoff)
            .OrderBy(t => t.CreatedAt)
            .Take(batchSize)
            .ToListAsync(ct);

        foreach (var transaction in stale)
        {
            var from = transaction.Status;
            transaction.Expire(now);
            audit.Record("transaction.expired", "Transaction", transaction.Id, new { from, to = transaction.Status }, transaction.TenantId);
            audit.Publish("transaction.expired", transaction.TenantId, Payload(transaction));
        }

        if (stale.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            foreach (var tenantId in stale.Select(t => t.TenantId).Distinct())
            {
                await InvalidateReportsAsync(tenantId, ct);
            }
        }

        return stale.Count;
    }

    private async Task<TransactionDto> TransitionAsync(
        Guid id, uint expectedVersion, string action, Action<Transaction, Guid, DateTimeOffset> apply, string? reason, CancellationToken ct)
    {
        var userId = context.RequireUserId();
        var transaction = await db.Transactions.FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException("Transaction", id);

        await access.EnsureBranchAccessAsync(transaction.BranchId, ct);
        db.UseExpectedVersion(transaction, expectedVersion);

        var from = transaction.Status;
        apply(transaction, userId, clock.GetUtcNow());

        audit.Record(action, "Transaction", transaction.Id, new { from, to = transaction.Status, reason }, transaction.TenantId);
        audit.Publish(action, transaction.TenantId, Payload(transaction));
        await db.SaveChangesAsync(ct);
        await InvalidateReportsAsync(transaction.TenantId, ct);

        return await GetAsync(id, ct);
    }

    private async Task<TransactionDto?> FindByIdempotencyKeyAsync(Guid userId, string key, CancellationToken ct)
    {
        var id = await db.Transactions.AsNoTracking().Where(t => t.CreatedByUserId == userId && t.IdempotencyKey == key).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(ct);
        return id is null ? null : await reads.GetAsync(id.Value, ct);
    }

    private async Task InvalidateReportsAsync(Guid tenantId, CancellationToken ct)
    {
        await cache.RemoveByTagAsync(CacheKeys.TransactionsTag(tenantId), ct);
        await cache.RemoveByTagAsync(CacheKeys.AllTransactionsTag, ct);
    }

    private static object Payload(Transaction t) => new
    {
        id = t.Id,
        t.Reference,
        t.Type,
        t.Status,
        t.Amount,
        t.Currency,
        t.Fee,
        t.NetAmount,
        t.BranchId,
        t.StatusReason,
    };

    /// <summary>Human readable, sortable reference: TRX-20261022-7KQ4M2XA (Crockford base32, no ambiguous characters).</summary>
    internal static string NewReference(DateTimeOffset now)
    {
        const string alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        Span<char> suffix = stackalloc char[8];
        for (var i = 0; i < suffix.Length; i++)
        {
            suffix[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        }

        return $"TRX-{now:yyyyMMdd}-{suffix}";
    }
}
