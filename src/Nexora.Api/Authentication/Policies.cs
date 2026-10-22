using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Nexora.Domain;

namespace Nexora.Api.Authentication;

/// <summary>
/// Endpoints authorize against named policies (what may be done), not raw roles (who someone is).
/// Every policy also carries <see cref="ActiveTenantRequirement"/>, so users of a suspended tenant are locked out
/// immediately, even with a still-valid access token.
/// </summary>
public static class Policies
{
    public const string PlatformAdmin = "platform:admin";
    public const string ManageTenant = "tenant:manage";
    public const string ViewUsers = "users:view";
    public const string CreateTransactions = "transactions:create";
    public const string ReviewTransactions = "transactions:review";
    public const string CancelTransactions = "transactions:cancel";
    public const string ViewReports = "reports:view";
    public const string ViewAudit = "audit:view";
    public const string BranchAccess = "branch:access";

    private static readonly Dictionary<string, string[]> RolePolicies = new()
    {
        [PlatformAdmin] = [Roles.PlatformAdmin],
        [ManageTenant] = [Roles.TenantAdmin, Roles.PlatformAdmin],
        [ViewUsers] = [Roles.TenantAdmin, Roles.Manager, Roles.Auditor, Roles.PlatformAdmin],
        [CreateTransactions] = [Roles.Operator, Roles.Manager, Roles.TenantAdmin, Roles.PlatformAdmin],
        [ReviewTransactions] = [Roles.Manager, Roles.TenantAdmin, Roles.PlatformAdmin],
        [CancelTransactions] = [Roles.Operator, Roles.Manager, Roles.TenantAdmin, Roles.PlatformAdmin],
        [ViewReports] = [Roles.Manager, Roles.TenantAdmin, Roles.Auditor, Roles.PlatformAdmin],
        [ViewAudit] = [Roles.Auditor, Roles.TenantAdmin, Roles.PlatformAdmin],
    };

    public static IServiceCollection AddNexoraAuthorization(this IServiceCollection services)
    {
        services.AddScoped<IAuthorizationHandler, ActiveTenantHandler>();
        services.AddSingleton<IAuthorizationHandler, BranchAccessHandler>();

        services.AddAuthorizationBuilder()
            .SetDefaultPolicy(Baseline())
            .SetFallbackPolicy(Baseline())
            .AddPolicy(BranchAccess, p => p.AddRequirements(new BranchAccessRequirement()));

        services.Configure<AuthorizationOptions>(options =>
        {
            foreach (var (name, roles) in RolePolicies)
            {
                options.AddPolicy(name, p => p.Combine(Baseline()).RequireRole(roles));
            }
        });

        return services;
    }

    private static AuthorizationPolicy Baseline() =>
        new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .AddRequirements(new ActiveTenantRequirement())
            .Build();
}
