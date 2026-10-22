using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nexora.Application.Abstractions;

namespace Nexora.Application.Services;

public sealed record MaintenanceReport(int ExpiredTransactions, int PurgedIdempotencyRecords, int PurgedRefreshTokens);

/// <summary>Periodic housekeeping, executed by the maintenance background worker in system scope.</summary>
public sealed class MaintenanceService(
    IApplicationDbContext db,
    TransactionService transactions,
    IOptions<TransactionOptions> options,
    TimeProvider clock,
    ILogger<MaintenanceService> logger)
{
    public async Task<MaintenanceReport> RunOnceAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();

        var expired = await transactions.ExpireStaleAsync(TimeSpan.FromHours(options.Value.PendingExpiryHours), batchSize: 500, ct);

        var purgedKeys = await db.IdempotencyRecords.Where(r => r.ExpiresAt < now).ExecuteDeleteAsync(ct);

        var tokenCutoff = now.AddDays(-7);
        var purgedTokens = await db.RefreshTokens
            .Where(t => t.ExpiresAt < tokenCutoff || (t.RevokedAt != null && t.RevokedAt < tokenCutoff))
            .ExecuteDeleteAsync(ct);

        if (expired + purgedKeys + purgedTokens > 0)
        {
            logger.LogInformation(
                "Maintenance run: {ExpiredTransactions} transactions expired, {PurgedIdempotencyRecords} idempotency records and {PurgedRefreshTokens} refresh tokens purged",
                expired, purgedKeys, purgedTokens);
        }

        return new MaintenanceReport(expired, purgedKeys, purgedTokens);
    }
}
