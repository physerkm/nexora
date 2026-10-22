using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Nexora.Application.Abstractions;
using Nexora.Application.Common;
using Nexora.Application.DTOs;
using Nexora.Domain;
using Nexora.Domain.Entities;

namespace Nexora.Application.Services;

/// <summary>
/// Tenant user management. Users have no global query filter (Identity must find them at sign-in before any
/// tenant is known), so every query here applies the tenant condition explicitly via <see cref="ScopedUsers"/>.
/// </summary>
public sealed class UserService(
    IApplicationDbContext db,
    UserManager<AppUser> users,
    AuthService auth,
    AuditTrail audit,
    RequestContext context,
    TimeProvider clock)
{
    public async Task<PagedResult<UserDto>> ListAsync(UserListQuery query, CancellationToken ct)
    {
        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        var scoped = ScopedUsers();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToUpperInvariant();
            scoped = scoped.Where(u => u.NormalizedEmail!.Contains(term) || u.FullName.ToUpper().Contains(term));
        }

        if (query.IsActive is { } active)
        {
            scoped = scoped.Where(u => u.IsActive == active);
        }

        var rows = Project(scoped);
        if (!string.IsNullOrWhiteSpace(query.Role))
        {
            rows = rows.Where(r => r.Role == query.Role);
        }

        var total = await rows.LongCountAsync(ct);
        var items = await rows.OrderBy(r => r.Email).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var now = clock.GetUtcNow();
        return PagedResult<UserDto>.Create(items.Select(r => r.ToDto(now)).ToList(), page, pageSize, total);
    }

    public async Task<UserDto> GetAsync(Guid id, CancellationToken ct)
    {
        var row = await Project(ScopedUsers().Where(u => u.Id == id)).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("User", id);
        return row.ToDto(clock.GetUtcNow());
    }

    public async Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken ct)
    {
        var tenantId = context.RequireTenantId();
        await EnsureBranchBelongsToTenantAsync(request.BranchId, tenantId, ct);

        var user = new AppUser
        {
            TenantId = tenantId,
            BranchId = request.BranchId,
            Email = request.Email.Trim(),
            UserName = request.Email.Trim(),
            FullName = request.FullName.Trim(),
            EmailConfirmed = true,
            CreatedAt = clock.GetUtcNow(),
        };

        await db.InTransactionAsync(async token =>
        {
            IdentityGuard.ThrowIfFailed(await users.CreateAsync(user, request.Password));
            IdentityGuard.ThrowIfFailed(await users.AddToRoleAsync(user, request.Role));
            audit.Record("user.created", "User", user.Id, new { user.Email, user.FullName, role = request.Role, user.BranchId });
            await db.SaveChangesAsync(token);
            return true;
        }, ct);

        return await GetAsync(user.Id, ct);
    }

    public async Task<UserDto> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken ct)
    {
        var user = await ScopedUsers().FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw new NotFoundException("User", id);

        if (user.Id == context.UserId && (!request.IsActive || request.Role != Roles.TenantAdmin))
        {
            throw new ConflictException("self_lockout", "You cannot deactivate yourself or remove your own admin role.");
        }

        await EnsureBranchBelongsToTenantAsync(request.BranchId, user.TenantId, ct);

        var currentRoles = await users.GetRolesAsync(user);
        var roleChanged = !currentRoles.Contains(request.Role);
        var deactivated = user.IsActive && !request.IsActive;
        var branchChanged = user.BranchId != request.BranchId;

        await db.InTransactionAsync(async token =>
        {
            user.FullName = request.FullName.Trim();
            user.BranchId = request.BranchId;
            user.IsActive = request.IsActive;
            IdentityGuard.ThrowIfFailed(await users.UpdateAsync(user));

            if (roleChanged)
            {
                IdentityGuard.ThrowIfFailed(await users.RemoveFromRolesAsync(user, currentRoles));
                IdentityGuard.ThrowIfFailed(await users.AddToRoleAsync(user, request.Role));
            }

            // Role, branch and status are baked into access tokens, so changing them must end existing sessions.
            if (roleChanged || deactivated || branchChanged)
            {
                await auth.RevokeAllUserSessionsAsync(user, "user_updated", token);
            }

            audit.Record("user.updated", "User", user.Id, new
            {
                user.FullName,
                role = request.Role,
                previousRoles = currentRoles,
                user.BranchId,
                user.IsActive,
                sessionsRevoked = roleChanged || deactivated || branchChanged,
            });
            await db.SaveChangesAsync(token);
            return true;
        }, ct);

        return await GetAsync(id, ct);
    }

    private IQueryable<AppUser> ScopedUsers()
    {
        if (context.BypassTenantFilter)
        {
            return db.Users.Where(u => u.TenantId != null);
        }

        var tenantId = context.RequireTenantId();
        return db.Users.Where(u => u.TenantId == tenantId);
    }

    private async Task EnsureBranchBelongsToTenantAsync(Guid? branchId, Guid? tenantId, CancellationToken ct)
    {
        if (branchId is { } id && !await db.Branches.AnyAsync(b => b.Id == id && b.TenantId == tenantId, ct))
        {
            throw new BadRequestException("invalid_branch", "The branch does not exist in this tenant.");
        }
    }

    // Member-init projection (not a constructor) so EF can keep composing Where/OrderBy on it in SQL.
    private IQueryable<UserRow> Project(IQueryable<AppUser> source) =>
        source.Select(u => new UserRow
        {
            Id = u.Id,
            TenantId = u.TenantId,
            Email = u.Email!,
            FullName = u.FullName,
            Role = (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id where ur.UserId == u.Id select r.Name).FirstOrDefault(),
            BranchId = u.BranchId,
            IsActive = u.IsActive,
            LockoutEnd = u.LockoutEnd,
            CreatedAt = u.CreatedAt,
            LastLoginAt = u.LastLoginAt,
        });

    private sealed class UserRow
    {
        public Guid Id { get; init; }
        public Guid? TenantId { get; init; }
        public string Email { get; init; } = null!;
        public string FullName { get; init; } = null!;
        public string? Role { get; init; }
        public Guid? BranchId { get; init; }
        public bool IsActive { get; init; }
        public DateTimeOffset? LockoutEnd { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset? LastLoginAt { get; init; }

        public UserDto ToDto(DateTimeOffset now) =>
            new(Id, TenantId, Email, FullName, Role, BranchId, IsActive, LockoutEnd > now, CreatedAt, LastLoginAt);
    }
}
