using System.Linq.Expressions;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Nexora.Application.Abstractions;
using Nexora.Application.Common;
using Nexora.Application.DTOs;
using Nexora.Domain.Entities;
using Nexora.Infrastructure.Persistence;

namespace Nexora.Infrastructure.Repositories;

/// <summary>
/// Read side for the (potentially very large) transactions table:
/// no tracking, index-aligned filters, ILIKE backed by trigram indexes, and keyset pagination using a
/// PostgreSQL row-value comparison <c>(created_at, id) &lt; (@c, @i)</c> that maps directly onto
/// <c>ix_transactions_tenant_created</c>.
/// </summary>
internal sealed class TransactionReadRepository(NexoraDbContext db) : ITransactionReadRepository
{
    public async Task<PagedResult<TransactionDto>> ListAsync(TransactionListQuery query, CancellationToken ct)
    {
        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        var descending = !string.Equals(query.SortDir, "asc", StringComparison.OrdinalIgnoreCase);
        var filtered = ApplyFilters(db.Transactions.AsNoTracking(), query);

        if (query.UseCursor)
        {
            if (query.Cursor is not null)
            {
                var (createdAt, id) = DecodeCursor(query.Cursor);
                filtered = descending
                    ? filtered.Where(t => EF.Functions.LessThan(ValueTuple.Create(t.CreatedAt, t.Id), ValueTuple.Create(createdAt, id)))
                    : filtered.Where(t => EF.Functions.GreaterThan(ValueTuple.Create(t.CreatedAt, t.Id), ValueTuple.Create(createdAt, id)));
            }

            var ordered = descending
                ? filtered.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
                : filtered.OrderBy(t => t.CreatedAt).ThenBy(t => t.Id);

            var rows = await ordered.Take(pageSize + 1).ToListAsync(ct);
            var hasMore = rows.Count > pageSize;
            if (hasMore)
            {
                rows.RemoveAt(rows.Count - 1);
            }

            var next = hasMore ? EncodeCursor(rows[^1].CreatedAt, rows[^1].Id) : null;
            return new PagedResult<TransactionDto>(await ToDtosAsync(rows, ct), page, pageSize, null, null, next);
        }

        var total = await filtered.LongCountAsync(ct);
        var pageRows = await Sort(filtered, query.SortBy, descending)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return PagedResult<TransactionDto>.Create(await ToDtosAsync(pageRows, ct), page, pageSize, total);
    }

    public async Task<TransactionDto?> GetAsync(Guid id, CancellationToken ct)
    {
        var transaction = await db.Transactions.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
        return transaction is null ? null : (await ToDtosAsync([transaction], ct))[0];
    }

    private static IQueryable<Transaction> ApplyFilters(IQueryable<Transaction> source, TransactionListQuery q)
    {
        if (q.Status is { } status) source = source.Where(t => t.Status == status);
        if (q.Type is { } type) source = source.Where(t => t.Type == type);
        if (q.BranchId is { } branchId) source = source.Where(t => t.BranchId == branchId);
        if (!string.IsNullOrWhiteSpace(q.Currency))
        {
            var currency = q.Currency.Trim().ToUpperInvariant();
            source = source.Where(t => t.Currency == currency);
        }

        if (q.From is { } from)
        {
            var fromUtc = from.ToUniversalTime();
            source = source.Where(t => t.CreatedAt >= fromUtc);
        }

        if (q.To is { } to)
        {
            var toUtc = to.ToUniversalTime();
            source = source.Where(t => t.CreatedAt <= toUtc);
        }

        if (q.MinAmount is { } min) source = source.Where(t => t.Amount >= min);
        if (q.MaxAmount is { } max) source = source.Where(t => t.Amount <= max);

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var pattern = Paging.ToContainsPattern(q.Search);
            source = source.Where(t =>
                EF.Functions.ILike(t.Reference, pattern) ||
                EF.Functions.ILike(t.CounterpartyName!, pattern) ||
                EF.Functions.ILike(t.Description!, pattern));
        }

        return source;
    }

    private static IOrderedQueryable<Transaction> Sort(IQueryable<Transaction> source, string? sortBy, bool descending)
    {
        IOrderedQueryable<Transaction> By<TKey>(Expression<Func<Transaction, TKey>> key) =>
            descending ? source.OrderByDescending(key) : source.OrderBy(key);

        var ordered = sortBy?.ToLowerInvariant() switch
        {
            "amount" => By(t => t.Amount),
            "status" => By(t => t.Status),
            "reference" => By(t => t.Reference),
            "type" => By(t => t.Type),
            _ => By(t => t.CreatedAt),
        };

        // Deterministic tie-breaker so offset pages never overlap or skip rows.
        return descending ? ordered.ThenByDescending(t => t.Id) : ordered.ThenBy(t => t.Id);
    }

    private async Task<IReadOnlyList<TransactionDto>> ToDtosAsync(IReadOnlyCollection<Transaction> rows, CancellationToken ct)
    {
        var branchIds = rows.Select(r => r.BranchId).Distinct().ToList();
        var names = await db.Branches.AsNoTracking()
            .Where(b => branchIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, b => b.Name, ct);

        return rows.Select(r => r.ToDto(names.GetValueOrDefault(r.BranchId, "?"))).ToList();
    }

    internal static string EncodeCursor(DateTimeOffset createdAt, Guid id)
    {
        var raw = $"{createdAt.UtcTicks}_{id:N}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    internal static (DateTimeOffset CreatedAt, Guid Id) DecodeCursor(string cursor)
    {
        try
        {
            var base64 = cursor.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(base64)).Split('_');
            return (new DateTimeOffset(long.Parse(parts[0]), TimeSpan.Zero), Guid.ParseExact(parts[1], "N"));
        }
        catch (Exception ex) when (ex is FormatException or IndexOutOfRangeException or ArgumentException or OverflowException)
        {
            throw new BadRequestException("invalid_cursor", "The cursor is malformed.");
        }
    }
}
