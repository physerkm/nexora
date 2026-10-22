using System.Text;
using Microsoft.EntityFrameworkCore;
using Nexora.Application.Abstractions;
using Nexora.Application.DTOs;
using Nexora.Domain;
using Nexora.Infrastructure.Persistence;
using Npgsql;

namespace Nexora.Infrastructure.Repositories;

/// <summary>
/// Dashboard aggregates. Every query is a single GROUP BY over the (tenant_id, status|branch_id, created_at)
/// indexes; the per-day series uses raw SQL because it needs date_trunc.
/// </summary>
internal sealed class ReportRepository(NexoraDbContext db, TimeProvider clock) : IReportRepository
{
    public async Task<ReportSummaryDto> GetSummaryAsync(Guid? tenantId, DateTimeOffset from, DateTimeOffset to, Guid? branchId, CancellationToken ct)
    {
        var fromUtc = from.ToUniversalTime();
        var toUtc = to.ToUniversalTime();

        var scope = db.Transactions.AsNoTracking().Where(t => t.CreatedAt >= fromUtc && t.CreatedAt < toUtc);
        if (tenantId is { } tid) scope = scope.Where(t => t.TenantId == tid);
        if (branchId is { } bid) scope = scope.Where(t => t.BranchId == bid);

        var statuses = await scope
            .GroupBy(t => t.Status)
            .Select(g => new StatusCountDto(g.Key, g.LongCount()))
            .ToListAsync(ct);

        var totals = await scope
            .GroupBy(t => t.Currency)
            .OrderBy(g => g.Key)
            .Select(g => new CurrencyTotalDto(
                g.Key,
                g.LongCount(),
                g.LongCount(t => t.Status == TransactionStatus.Approved),
                g.Sum(t => t.Status == TransactionStatus.Approved ? t.Amount : 0m),
                g.Sum(t => t.Status == TransactionStatus.Approved ? t.Fee : 0m)))
            .ToListAsync(ct);

        var branchRows = await scope
            .GroupBy(t => t.BranchId)
            .Select(g => new { BranchId = g.Key, Count = g.LongCount(), Approved = g.LongCount(t => t.Status == TransactionStatus.Approved) })
            .OrderByDescending(x => x.Count)
            .Take(20)
            .ToListAsync(ct);

        var ids = branchRows.Select(b => b.BranchId).ToList();
        var names = await db.Branches.AsNoTracking().Where(b => ids.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.Name, ct);

        var daily = await GetDailyAsync(tenantId, fromUtc, toUtc, branchId, ct);

        return new ReportSummaryDto(
            fromUtc,
            toUtc,
            clock.GetUtcNow(),
            totals,
            statuses.OrderBy(s => s.Status).ToList(),
            branchRows.Select(b => new BranchSummaryDto(b.BranchId, names.GetValueOrDefault(b.BranchId, "?"), b.Count, b.Approved)).ToList(),
            daily);
    }

    private async Task<List<DailyPointDto>> GetDailyAsync(Guid? tenantId, DateTimeOffset from, DateTimeOffset to, Guid? branchId, CancellationToken ct)
    {
        // Raw SQL is not covered by EF query filters, so the tenant predicate is applied explicitly.
        var sql = new StringBuilder("""
            SELECT (created_at AT TIME ZONE 'UTC')::date AS day,
                   count(*) AS count,
                   count(*) FILTER (WHERE status = 'Approved') AS approved_count
            FROM transactions
            WHERE created_at >= @from AND created_at < @to
            """);
        var parameters = new List<object> { new NpgsqlParameter("from", from), new NpgsqlParameter("to", to) };

        if (tenantId is { } tid)
        {
            sql.Append(" AND tenant_id = @tenant");
            parameters.Add(new NpgsqlParameter("tenant", tid));
        }
        else if (!db.BypassTenantFilter)
        {
            return [];
        }

        if (branchId is { } bid)
        {
            sql.Append(" AND branch_id = @branch");
            parameters.Add(new NpgsqlParameter("branch", bid));
        }

        sql.Append(" GROUP BY 1 ORDER BY 1");

        var rows = await db.Database.SqlQueryRaw<DailyRow>(sql.ToString(), parameters.ToArray()).ToListAsync(ct);
        return rows.Select(r => new DailyPointDto(r.Day, r.Count, r.ApprovedCount)).ToList();
    }

    private sealed class DailyRow
    {
        public DateOnly Day { get; set; }
        public long Count { get; set; }
        public long ApprovedCount { get; set; }
    }
}
