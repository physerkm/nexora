using Microsoft.EntityFrameworkCore;
using Nexora.Application.Abstractions;
using Nexora.Application.Common;
using Nexora.Application.DTOs;
using Nexora.Domain.Entities;

namespace Nexora.Application.Services;

public sealed class BranchService(IApplicationDbContext db, AuditTrail audit, RequestContext context, TimeProvider clock)
{
    public async Task<PagedResult<BranchDto>> ListAsync(BranchListQuery query, CancellationToken ct)
    {
        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);

        // The global tenant query filter scopes this to the caller's tenant.
        var branches = db.Branches.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            branches = branches.Where(b => b.Name.ToLower().Contains(term) || b.Code.ToLower().Contains(term));
        }

        if (query.IsActive is { } active)
        {
            branches = branches.Where(b => b.IsActive == active);
        }

        var total = await branches.LongCountAsync(ct);
        var items = await branches.OrderBy(b => b.Code).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return PagedResult<BranchDto>.Create(items.Select(b => b.ToDto()).ToList(), page, pageSize, total);
    }

    public async Task<BranchDto> GetAsync(Guid id, CancellationToken ct) =>
        (await db.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct))?.ToDto()
        ?? throw new NotFoundException("Branch", id);

    public async Task<BranchDto> CreateAsync(CreateBranchRequest request, CancellationToken ct)
    {
        var tenantId = context.RequireTenantId();
        var branch = Branch.Create(tenantId, request.Code, request.Name, request.City, clock.GetUtcNow());
        db.Branches.Add(branch);
        audit.Record("branch.created", "Branch", branch.Id, new { branch.Code, branch.Name, branch.City });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (UniqueConstraintException)
        {
            throw new ConflictException("branch_code_taken", $"Branch code '{branch.Code}' is already in use.");
        }

        return branch.ToDto();
    }

    public async Task<BranchDto> UpdateAsync(Guid id, UpdateBranchRequest request, CancellationToken ct)
    {
        var branch = await db.Branches.FirstOrDefaultAsync(b => b.Id == id, ct) ?? throw new NotFoundException("Branch", id);
        db.UseExpectedVersion(branch, request.Version);

        var before = new { branch.Name, branch.City, branch.IsActive };
        branch.Update(request.Name, request.City, request.IsActive, clock.GetUtcNow());
        audit.Record("branch.updated", "Branch", branch.Id, new { before, after = new { branch.Name, branch.City, branch.IsActive } }, branch.TenantId);
        await db.SaveChangesAsync(ct);
        return branch.ToDto();
    }
}
