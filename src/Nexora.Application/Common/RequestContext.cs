using Nexora.Domain;

namespace Nexora.Application.Common;

public interface ITenantContext
{
    /// <summary>The tenant the current request operates on. Null for anonymous calls and for platform admins without a selected tenant.</summary>
    Guid? TenantId { get; }

    /// <summary>True only for platform admins without a selected tenant and for background jobs (system scope).</summary>
    bool BypassTenantFilter { get; }

    Guid RequireTenantId();
}

public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid? UserId { get; }
    string? Email { get; }
    Guid? BranchId { get; }
    IReadOnlyCollection<string> Roles { get; }
    bool IsInRole(string role);
    Guid RequireUserId();
}

public interface IRequestInfo
{
    string? CorrelationId { get; }
    string? IpAddress { get; }
}

/// <summary>
/// Scoped, per-request ambient context. The API fills it from the validated JWT (see RequestContextMiddleware);
/// background jobs switch it into system scope. Defaults are fail-closed: no tenant and no bypass means queries
/// on tenant-owned tables return nothing.
/// </summary>
public sealed class RequestContext : ITenantContext, ICurrentUser, IRequestInfo
{
    private HashSet<string> _roles = new(StringComparer.Ordinal);

    public Guid? TenantId { get; private set; }
    public bool BypassTenantFilter { get; private set; }
    public bool IsAuthenticated { get; private set; }
    public Guid? UserId { get; private set; }
    public string? Email { get; private set; }
    public Guid? BranchId { get; private set; }
    public IReadOnlyCollection<string> Roles => _roles;
    public string? CorrelationId { get; private set; }
    public string? IpAddress { get; private set; }
    public bool IsSystem { get; private set; }

    public bool IsInRole(string role) => _roles.Contains(role);

    public bool IsPlatformAdmin => IsInRole(Domain.Roles.PlatformAdmin);

    public Guid RequireTenantId() => TenantId ?? throw new BadRequestException(
        "tenant_required",
        IsPlatformAdmin
            ? "This operation needs a tenant. Send the X-Tenant-Id header to act on behalf of a tenant."
            : "No tenant is associated with the current request.");

    public Guid RequireUserId() => UserId ?? throw new UnauthorizedException("unauthenticated", "Authentication is required.");

    public void SetRequest(string? correlationId, string? ipAddress)
    {
        CorrelationId = correlationId;
        IpAddress = ipAddress;
    }

    public void SetUser(Guid userId, string? email, Guid? tenantId, Guid? branchId, IEnumerable<string> roles)
    {
        IsAuthenticated = true;
        UserId = userId;
        Email = email;
        TenantId = tenantId;
        BranchId = branchId;
        _roles = new HashSet<string>(roles, StringComparer.Ordinal);
        BypassTenantFilter = false;
    }

    /// <summary>Platform admins either act on one selected tenant or (read-only views) across all tenants.</summary>
    public void SelectTenant(Guid? tenantId)
    {
        if (!IsPlatformAdmin)
        {
            throw new ForbiddenException("Only platform administrators can switch tenants.");
        }

        TenantId = tenantId;
        BypassTenantFilter = tenantId is null;
    }

    public void UseSystemScope(Guid? tenantId = null)
    {
        IsSystem = true;
        TenantId = tenantId;
        BypassTenantFilter = tenantId is null;
    }
}
