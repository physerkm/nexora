using Microsoft.EntityFrameworkCore;
using Nexora.Application.Abstractions;
using Nexora.Domain;
using Nexora.Infrastructure.Persistence;
using Npgsql;
using NpgsqlTypes;

namespace Nexora.Infrastructure.Repositories;

/// <summary>
/// Reservation-first idempotency. <c>INSERT ... ON CONFLICT DO NOTHING</c> on the (user_id, key) unique index
/// makes the reservation atomic, so of two concurrent identical requests exactly one executes.
/// All writes bypass the change tracker so a failed business operation can never be re-flushed here.
/// </summary>
internal sealed class IdempotencyStore(NexoraDbContext db, TimeProvider clock) : IIdempotencyStore
{
    public async Task<IdempotencyReservation> TryReserveAsync(
        Guid? tenantId, Guid userId, string key, string method, string path, string requestHash, TimeSpan ttl, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var now = clock.GetUtcNow();
            var id = Guid.CreateVersion7();

            var inserted = await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO idempotency_records
                    (id, tenant_id, user_id, key, request_method, request_path, request_hash, status, created_at, expires_at)
                VALUES (@id, @tenant, @user, @key, @method, @path, @hash, @status, @now, @expires)
                ON CONFLICT (user_id, key) DO NOTHING
                """,
                [
                    new NpgsqlParameter("id", id),
                    new NpgsqlParameter("tenant", NpgsqlDbType.Uuid) { Value = (object?)tenantId ?? DBNull.Value },
                    new NpgsqlParameter("user", userId),
                    new NpgsqlParameter("key", key),
                    new NpgsqlParameter("method", method),
                    new NpgsqlParameter("path", path),
                    new NpgsqlParameter("hash", requestHash),
                    new NpgsqlParameter("status", nameof(IdempotencyStatus.InProgress)),
                    new NpgsqlParameter("now", now),
                    new NpgsqlParameter("expires", now.Add(ttl)),
                ],
                ct);

            if (inserted == 1)
            {
                return new IdempotencyReservation(IdempotencyOutcome.Reserved, id);
            }

            var existing = await db.IdempotencyRecords.AsNoTracking()
                .FirstOrDefaultAsync(r => r.UserId == userId && r.Key == key, ct);

            if (existing is null)
            {
                continue; // deleted between our INSERT and SELECT; try again
            }

            if (existing.ExpiresAt <= now)
            {
                await db.IdempotencyRecords.Where(r => r.Id == existing.Id).ExecuteDeleteAsync(ct);
                continue;
            }

            if (existing.RequestHash != requestHash || existing.RequestMethod != method || existing.RequestPath != path)
            {
                return new IdempotencyReservation(IdempotencyOutcome.PayloadMismatch, existing.Id);
            }

            return existing.Status == IdempotencyStatus.Completed
                ? new IdempotencyReservation(IdempotencyOutcome.Replay, existing.Id, existing.ResponseStatusCode, existing.ResponseBody)
                : new IdempotencyReservation(IdempotencyOutcome.InProgress, existing.Id);
        }

        return new IdempotencyReservation(IdempotencyOutcome.InProgress, Guid.Empty);
    }

    public async Task CompleteAsync(Guid recordId, int statusCode, string? body, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await db.IdempotencyRecords
            .Where(r => r.Id == recordId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, IdempotencyStatus.Completed)
                .SetProperty(r => r.ResponseStatusCode, statusCode)
                .SetProperty(r => r.ResponseBody, body)
                .SetProperty(r => r.CompletedAt, now), ct);
    }

    public Task ReleaseAsync(Guid recordId, CancellationToken ct) =>
        db.IdempotencyRecords.Where(r => r.Id == recordId).ExecuteDeleteAsync(ct);
}
