using Microsoft.Extensions.Caching.Hybrid;
using Nexora.Application.Abstractions;
using Nexora.Application.Common;
using Nexora.Application.DTOs;

namespace Nexora.Application.Services;

public sealed record ReportResult(ReportSummaryDto Summary, bool CacheHit);

/// <summary>
/// Dashboard aggregates scan many rows, so results are cached briefly (L1 memory + optional Redis L2)
/// and invalidated by tag whenever a transaction of the tenant changes. The endpoint exposes
/// <c>X-Cache</c> and <c>Server-Timing</c> headers so the benefit can be measured instead of assumed.
/// </summary>
public sealed class ReportService(IReportRepository reports, HybridCache cache, RequestContext context, TimeProvider clock)
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromSeconds(60),
        LocalCacheExpiration = TimeSpan.FromSeconds(30),
    };

    public async Task<ReportResult> GetSummaryAsync(ReportSummaryQuery query, CancellationToken ct)
    {
        var tenantId = context.BypassTenantFilter ? (Guid?)null : context.RequireTenantId();

        // Round the window to whole minutes so repeated dashboard loads hit the same cache entry.
        var to = Truncate(query.To ?? clock.GetUtcNow().AddMinutes(1));
        var from = Truncate(query.From ?? to.AddDays(-30));
        var branchId = query.BranchId;

        var key = $"report:summary:{tenantId?.ToString() ?? "all"}:{from:yyyyMMddHHmm}:{to:yyyyMMddHHmm}:{branchId}";
        var computed = false;

        var summary = await cache.GetOrCreateAsync(
            key,
            async token =>
            {
                computed = true;
                return await reports.GetSummaryAsync(tenantId, from, to, branchId, token);
            },
            CacheOptions,
            [CacheKeys.TransactionsTag(tenantId)],
            ct);

        return new ReportResult(summary, CacheHit: !computed);
    }

    private static DateTimeOffset Truncate(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, TimeSpan.Zero);
    }
}
