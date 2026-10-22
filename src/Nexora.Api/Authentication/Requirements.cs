using Microsoft.AspNetCore.Authorization;
using Nexora.Application.Abstractions;
using Nexora.Application.Common;
using Nexora.Application.Security;
using Nexora.Application.Services;
using Nexora.Domain;

namespace Nexora.Api.Authentication;

public sealed class ActiveTenantRequirement : IAuthorizationRequirement;

/// <summary>Fails when the caller's tenant is suspended. Platform admins (no tenant claim) pass.</summary>
public sealed class ActiveTenantHandler(TenantStatusProvider tenants) : AuthorizationHandler<ActiveTenantRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, ActiveTenantRequirement requirement)
    {
        var claim = context.User.FindFirst(NexoraClaims.TenantId)?.Value;
        if (claim is null)
        {
            context.Succeed(requirement);
            return;
        }

        if (Guid.TryParse(claim, out var tenantId) && await tenants.GetStatusAsync(tenantId, CancellationToken.None) == TenantStatus.Active)
        {
            context.Succeed(requirement);
            return;
        }

        context.Fail(new AuthorizationFailureReason(this, "The tenant is suspended."));
    }
}

public sealed class BranchAccessRequirement : IAuthorizationRequirement;

public sealed record BranchResource(Guid BranchId);

/// <summary>Resource-based rule: users bound to a branch may only act on that branch's transactions.</summary>
public sealed class BranchAccessHandler : AuthorizationHandler<BranchAccessRequirement, BranchResource>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, BranchAccessRequirement requirement, BranchResource resource)
    {
        var claim = context.User.FindFirst(NexoraClaims.BranchId)?.Value;
        if (claim is null || (Guid.TryParse(claim, out var branchId) && branchId == resource.BranchId))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

/// <summary>Bridges the application layer's <see cref="IAccessPolicy"/> to ASP.NET Core's policy engine.</summary>
public sealed class HttpAccessPolicy(IAuthorizationService authorization, IHttpContextAccessor accessor) : IAccessPolicy
{
    public async Task EnsureBranchAccessAsync(Guid branchId, CancellationToken ct = default)
    {
        var user = accessor.HttpContext?.User;
        if (user is null)
        {
            return; // background jobs run without a user and are trusted
        }

        var result = await authorization.AuthorizeAsync(user, new BranchResource(branchId), Policies.BranchAccess);
        if (!result.Succeeded)
        {
            throw new ForbiddenException("You can only operate on transactions of your own branch.");
        }
    }
}
