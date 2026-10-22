using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Nexora.Application.Abstractions;
using Nexora.Application.Common;
using Nexora.Application.DTOs;
using Nexora.Application.Security;
using Nexora.Domain;
using Nexora.Domain.Entities;

namespace Nexora.Application.Services;

/// <summary>Platform-level tenant management (platform admins only) and the tenant's own settings.</summary>
public sealed class TenantService(
    IApplicationDbContext db,
    UserManager<AppUser> users,
    AuditTrail audit,
    TenantStatusProvider tenantStatus,
    RequestContext context,
    TimeProvider clock)
{
    public async Task<PagedResult<TenantDto>> ListAsync(TenantListQuery query, CancellationToken ct)
    {
        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        var tenants = db.Tenants.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            tenants = tenants.Where(t => t.Name.ToLower().Contains(term) || t.Slug.Contains(term));
        }

        if (query.Status is { } status)
        {
            tenants = tenants.Where(t => t.Status == status);
        }

        var total = await tenants.LongCountAsync(ct);
        var items = await Project(tenants.OrderBy(t => t.Name).Skip((page - 1) * pageSize).Take(pageSize)).ToListAsync(ct);
        return PagedResult<TenantDto>.Create(items, page, pageSize, total);
    }

    public async Task<TenantDto> GetAsync(Guid id, CancellationToken ct) =>
        await Project(db.Tenants.AsNoTracking().Where(t => t.Id == id)).FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException("Tenant", id);

    /// <summary>Creates the tenant and its first administrator atomically (several SaveChanges inside one DB transaction).</summary>
    public async Task<TenantDto> CreateAsync(CreateTenantRequest request, CancellationToken ct)
    {
        var slug = request.Slug.Trim().ToLowerInvariant();
        if (await db.Tenants.AnyAsync(t => t.Slug == slug, ct))
        {
            throw new ConflictException("slug_taken", $"A tenant with slug '{slug}' already exists.");
        }

        if (await users.FindByEmailAsync(request.AdminEmail) is not null)
        {
            throw new ConflictException("email_taken", "A user with this email already exists.");
        }

        var tenantId = await db.InTransactionAsync(async token =>
        {
            var now = clock.GetUtcNow();
            var tenant = Tenant.Create(request.Name, slug, now);
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync(token);

            var admin = new AppUser
            {
                TenantId = tenant.Id,
                Email = request.AdminEmail.Trim(),
                UserName = request.AdminEmail.Trim(),
                FullName = request.AdminFullName.Trim(),
                EmailConfirmed = true,
                CreatedAt = now,
            };

            IdentityGuard.ThrowIfFailed(await users.CreateAsync(admin, request.AdminPassword));
            IdentityGuard.ThrowIfFailed(await users.AddToRoleAsync(admin, Roles.TenantAdmin));

            audit.Record("tenant.created", "Tenant", tenant.Id, new { tenant.Name, tenant.Slug, adminUserId = admin.Id, adminEmail = admin.Email }, tenant.Id);
            await db.SaveChangesAsync(token);
            return tenant.Id;
        }, ct);

        return await GetAsync(tenantId, ct);
    }

    public async Task<TenantDto> ChangeStatusAsync(Guid id, ChangeTenantStatusRequest request, CancellationToken ct)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException("Tenant", id);
        db.UseExpectedVersion(tenant, request.Version);

        var previous = tenant.Status;
        tenant.ChangeStatus(request.Status, clock.GetUtcNow());
        audit.Record("tenant.status_changed", "Tenant", tenant.Id, new { from = previous, to = request.Status }, tenant.Id);
        await db.SaveChangesAsync(ct);
        await tenantStatus.InvalidateAsync(tenant.Id, ct);

        return await GetAsync(id, ct);
    }

    public async Task<TenantSettingsDto> GetCurrentAsync(CancellationToken ct)
    {
        var tenantId = context.RequireTenantId();
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, ct)
                     ?? throw new NotFoundException("Tenant", tenantId);
        return tenant.ToSettingsDto();
    }

    public async Task<TenantSettingsDto> UpdateCurrentAsync(UpdateTenantSettingsRequest request, CancellationToken ct)
    {
        var tenantId = context.RequireTenantId();
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct) ?? throw new NotFoundException("Tenant", tenantId);
        db.UseExpectedVersion(tenant, request.Version);

        var now = clock.GetUtcNow();
        tenant.Rename(request.Name, now);

        var secret = tenant.WebhookSecret;
        if (secret is null || request.RotateWebhookSecret)
        {
            secret = TokenHasher.NewWebhookSecret();
        }

        tenant.ConfigureWebhook(request.WebhookUrl, secret, now);
        audit.Record("tenant.settings_updated", "Tenant", tenant.Id,
            new { tenant.Name, tenant.WebhookUrl, secretRotated = request.RotateWebhookSecret });
        await db.SaveChangesAsync(ct);
        return tenant.ToSettingsDto();
    }

    private IQueryable<TenantDto> Project(IQueryable<Tenant> tenants) =>
        tenants.Select(t => new TenantDto(
            t.Id, t.Name, t.Slug, t.Status, t.WebhookUrl, t.CreatedAt, t.Version,
            db.Users.Count(u => u.TenantId == t.Id),
            db.Branches.IgnoreQueryFilters().Count(b => b.TenantId == t.Id)));
}

internal static class IdentityGuard
{
    public static void ThrowIfFailed(IdentityResult result)
    {
        if (result.Succeeded)
        {
            return;
        }

        var duplicate = result.Errors.Any(e => e.Code is "DuplicateEmail" or "DuplicateUserName");
        var message = string.Join(" ", result.Errors.Select(e => e.Description));
        throw duplicate ? new ConflictException("email_taken", message) : new BadRequestException("identity_error", message);
    }
}
