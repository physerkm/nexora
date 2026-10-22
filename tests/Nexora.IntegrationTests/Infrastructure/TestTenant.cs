using System.Net;
using Nexora.Application.DTOs;
using Nexora.Domain;

namespace Nexora.IntegrationTests.Infrastructure;

/// <summary>
/// A fully provisioned tenant created through the public API: two branches, an admin, a manager,
/// a second manager, a branch-bound operator, an unbound operator and an auditor.
/// </summary>
public sealed record TestTenant(
    Guid Id,
    string Slug,
    Guid MainBranchId,
    Guid OtherBranchId,
    string AdminEmail,
    string ManagerEmail,
    string Manager2Email,
    string OperatorEmail,
    string FreeOperatorEmail,
    string AuditorEmail)
{
    public static async Task<TestTenant> CreateAsync(NexoraApiFactory factory, string prefix)
    {
        var slug = $"{prefix}-{Guid.NewGuid():N}"[..20];
        var platform = await factory.LoginAsync(NexoraApiFactory.PlatformAdminEmail);

        var adminEmail = $"admin@{slug}.test";
        var created = await platform.PostAsync("/api/v1/platform/tenants",
            new CreateTenantRequest($"{prefix} Corp", slug, adminEmail, "Tenant Admin", NexoraApiFactory.Password));
        await created.EnsureStatusAsync(HttpStatusCode.Created);
        var tenant = await created.ReadAsync<TenantDto>();

        var admin = await factory.LoginAsync(adminEmail);
        var main = await CreateBranchAsync(admin, "MAIN");
        var other = await CreateBranchAsync(admin, "OTHER");

        var result = new TestTenant(tenant.Id, slug, main, other, adminEmail,
            $"manager@{slug}.test", $"manager2@{slug}.test", $"operator@{slug}.test", $"operator2@{slug}.test", $"auditor@{slug}.test");

        await CreateUserAsync(admin, result.ManagerEmail, Roles.Manager, null);
        await CreateUserAsync(admin, result.Manager2Email, Roles.Manager, null);
        await CreateUserAsync(admin, result.OperatorEmail, Roles.Operator, main);
        await CreateUserAsync(admin, result.FreeOperatorEmail, Roles.Operator, null);
        await CreateUserAsync(admin, result.AuditorEmail, Roles.Auditor, null);

        return result;
    }

    public static async Task<Guid> CreateUserAsync(ApiClient admin, string email, string role, Guid? branchId)
    {
        var response = await admin.PostAsync("/api/v1/users", new CreateUserRequest(email, "Test " + role, NexoraApiFactory.Password, role, branchId));
        await response.EnsureStatusAsync(HttpStatusCode.Created);
        return (await response.ReadAsync<UserDto>()).Id;
    }

    private static async Task<Guid> CreateBranchAsync(ApiClient admin, string code)
    {
        var response = await admin.PostAsync("/api/v1/branches", new CreateBranchRequest(code, code + " branch", "İstanbul"));
        await response.EnsureStatusAsync(HttpStatusCode.Created);
        return (await response.ReadAsync<BranchDto>()).Id;
    }
}
