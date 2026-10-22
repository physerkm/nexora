using Microsoft.EntityFrameworkCore;
using Nexora.Application.Abstractions;
using Nexora.Application.Common;
using Nexora.Application.DTOs;

namespace Nexora.Application.Services;

/// <summary>Read-only access to the audit trail. There is intentionally no update or delete API.</summary>
public sealed class AuditLogService(IApplicationDbContext db)
{
    public async Task<PagedResult<AuditLogDto>> ListAsync(AuditLogQuery query, CancellationToken ct)
    {
        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        var logs = db.AuditLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.EntityType))
        {
            logs = logs.Where(a => a.EntityType == query.EntityType);
        }

        if (!string.IsNullOrWhiteSpace(query.EntityId))
        {
            logs = logs.Where(a => a.EntityId == query.EntityId);
        }

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            logs = logs.Where(a => a.Action.StartsWith(query.Action));
        }

        if (query.UserId is { } userId)
        {
            logs = logs.Where(a => a.UserId == userId);
        }

        if (query.From is { } from)
        {
            logs = logs.Where(a => a.CreatedAt >= from);
        }

        if (query.To is { } to)
        {
            logs = logs.Where(a => a.CreatedAt <= to);
        }

        var total = await logs.LongCountAsync(ct);
        var items = await logs.OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        return PagedResult<AuditLogDto>.Create(items.Select(a => a.ToDto()).ToList(), page, pageSize, total);
    }
}
