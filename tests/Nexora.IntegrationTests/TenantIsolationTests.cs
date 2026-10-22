using System.Net;
using Nexora.Application.Common;
using Nexora.Application.DTOs;
using Nexora.Domain;
using Nexora.IntegrationTests.Infrastructure;

namespace Nexora.IntegrationTests;

/// <summary>Company A must never see or touch company B's data — through any endpoint.</summary>
[Collection(ApiCollection.Name)]
public sealed class TenantIsolationTests(NexoraApiFactory factory)
{
    private TestTenant A => factory.TenantA;
    private TestTenant B => factory.TenantB;

    [Fact]
    public async Task Tenant_cannot_read_or_change_another_tenants_transaction()
    {
        var operatorA = await factory.LoginAsync(A.OperatorEmail);
        var tx = await operatorA.CreateTransactionAsync(A.MainBranchId, 321m);

        var managerB = await factory.LoginAsync(B.ManagerEmail);

        await (await managerB.GetAsync($"/api/v1/transactions/{tx.Id}")).AssertProblemAsync(HttpStatusCode.NotFound, "not_found");
        await (await managerB.GetAsync($"/api/v1/transactions/{tx.Id}/history")).AssertProblemAsync(HttpStatusCode.NotFound);
        await (await managerB.PostAsync($"/api/v1/transactions/{tx.Id}/approve", new ApproveTransactionRequest(tx.Version)))
            .AssertProblemAsync(HttpStatusCode.NotFound);
        await (await managerB.PostAsync($"/api/v1/transactions/{tx.Id}/cancel", new TransactionReasonRequest(tx.Version, "x")))
            .AssertProblemAsync(HttpStatusCode.NotFound);

        // Still pending and untouched for its owner.
        var managerA = await factory.LoginAsync(A.ManagerEmail);
        var reloaded = await managerA.GetJsonAsync<TransactionDto>($"/api/v1/transactions/{tx.Id}");
        Assert.Equal(TransactionStatus.Pending, reloaded.Status);
    }

    [Fact]
    public async Task Lists_only_contain_own_tenant_rows()
    {
        var operatorA = await factory.LoginAsync(A.OperatorEmail);
        var marker = $"isolation-{Guid.NewGuid():N}";
        await operatorA.CreateTransactionAsync(A.MainBranchId, description: marker);

        var adminB = await factory.LoginAsync(B.AdminEmail);
        var auditorB = await factory.LoginAsync(B.AuditorEmail);

        var transactions = await adminB.GetJsonAsync<PagedResult<TransactionDto>>($"/api/v1/transactions?search={marker}");
        Assert.Empty(transactions.Items);
        Assert.All((await adminB.GetJsonAsync<PagedResult<TransactionDto>>("/api/v1/transactions?pageSize=100")).Items, t => Assert.Equal(B.Id, t.TenantId));

        Assert.All((await adminB.GetJsonAsync<PagedResult<BranchDto>>("/api/v1/branches")).Items, b => Assert.Equal(B.Id, b.TenantId));
        Assert.All((await adminB.GetJsonAsync<PagedResult<UserDto>>("/api/v1/users?pageSize=100")).Items, u => Assert.Equal(B.Id, u.TenantId));
        Assert.All((await auditorB.GetJsonAsync<PagedResult<AuditLogDto>>("/api/v1/audit-logs?pageSize=100")).Items, a => Assert.Equal(B.Id, a.TenantId));

        var report = await adminB.GetJsonAsync<ReportSummaryDto>("/api/v1/reports/summary");
        Assert.DoesNotContain(report.Branches, b => b.BranchId == A.MainBranchId);
    }

    [Fact]
    public async Task Tenant_cannot_access_another_tenants_branch_or_user_by_id()
    {
        var adminB = await factory.LoginAsync(B.AdminEmail);
        await (await adminB.GetAsync($"/api/v1/branches/{A.MainBranchId}")).AssertProblemAsync(HttpStatusCode.NotFound);
        await (await adminB.PutAsync($"/api/v1/branches/{A.MainBranchId}", new UpdateBranchRequest("hijack", null, true, 1)))
            .AssertProblemAsync(HttpStatusCode.NotFound);

        var adminA = await factory.LoginAsync(A.AdminEmail);
        var userA = adminA.Session!.User.Id;
        await (await adminB.GetAsync($"/api/v1/users/{userA}")).AssertProblemAsync(HttpStatusCode.NotFound);
        await (await adminB.PutAsync($"/api/v1/users/{userA}", new UpdateUserRequest("Hijacker Name", Roles.Auditor, null, false)))
            .AssertProblemAsync(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Creating_a_transaction_in_another_tenants_branch_is_rejected()
    {
        var managerB = await factory.LoginAsync(B.ManagerEmail);
        var response = await managerB.PostAsync("/api/v1/transactions",
            new CreateTransactionRequest(A.MainBranchId, TransactionType.Payment, 10m, "TRY", null, null), Guid.NewGuid().ToString());
        await response.AssertProblemAsync(HttpStatusCode.BadRequest, "invalid_branch");
    }

    [Fact]
    public async Task Users_cannot_switch_tenant_with_the_header()
    {
        var managerB = await factory.LoginAsync(B.ManagerEmail);
        managerB.TenantHeader = A.Id;
        await (await managerB.GetAsync("/api/v1/transactions")).AssertProblemAsync(HttpStatusCode.Forbidden, "cross_tenant_access");
    }

    [Fact]
    public async Task Platform_admin_sees_all_tenants_and_can_scope_to_one()
    {
        var operatorA = await factory.LoginAsync(A.OperatorEmail);
        var operatorB = await factory.LoginAsync(B.OperatorEmail);
        await operatorA.CreateTransactionAsync(A.MainBranchId);
        await operatorB.CreateTransactionAsync(B.MainBranchId);

        var platform = await factory.LoginAsync(NexoraApiFactory.PlatformAdminEmail);
        var all = await platform.GetJsonAsync<PagedResult<TransactionDto>>("/api/v1/transactions?pageSize=100");
        Assert.Contains(all.Items, t => t.TenantId == A.Id);
        Assert.Contains(all.Items, t => t.TenantId == B.Id);

        platform.TenantHeader = A.Id;
        var scoped = await platform.GetJsonAsync<PagedResult<TransactionDto>>("/api/v1/transactions?pageSize=100");
        Assert.All(scoped.Items, t => Assert.Equal(A.Id, t.TenantId));

        platform.TenantHeader = Guid.NewGuid();
        await (await platform.GetAsync("/api/v1/transactions")).AssertProblemAsync(HttpStatusCode.BadRequest, "unknown_tenant");
    }

    [Fact]
    public async Task Platform_admin_must_choose_a_tenant_before_writing()
    {
        var platform = await factory.LoginAsync(NexoraApiFactory.PlatformAdminEmail);
        var response = await platform.PostAsync("/api/v1/branches", new CreateBranchRequest("PLAT", "Platform branch", null));
        await response.AssertProblemAsync(HttpStatusCode.BadRequest, "tenant_required");

        platform.TenantHeader = A.Id;
        var created = await platform.PostAsync("/api/v1/branches", new CreateBranchRequest($"P{Guid.NewGuid():N}"[..10], "Platform branch", null));
        await created.EnsureStatusAsync(HttpStatusCode.Created);
        Assert.Equal(A.Id, (await created.ReadAsync<BranchDto>()).TenantId);
    }

    [Fact]
    public async Task Suspending_a_tenant_locks_out_its_users_immediately()
    {
        var tenant = await TestTenant.CreateAsync(factory, "suspend");
        var manager = await factory.LoginAsync(tenant.ManagerEmail);
        await (await manager.GetAsync("/api/v1/transactions")).EnsureStatusAsync(HttpStatusCode.OK);

        var platform = await factory.LoginAsync(NexoraApiFactory.PlatformAdminEmail);
        var current = await platform.GetJsonAsync<TenantDto>($"/api/v1/platform/tenants/{tenant.Id}");
        var suspend = await platform.PatchAsync($"/api/v1/platform/tenants/{tenant.Id}/status", new ChangeTenantStatusRequest(TenantStatus.Suspended, current.Version));
        await suspend.EnsureStatusAsync(HttpStatusCode.OK);

        await (await manager.GetAsync("/api/v1/transactions")).AssertProblemAsync(HttpStatusCode.Forbidden);
        var login = await factory.Anonymous().PostAsync("/api/v1/auth/login", new LoginRequest(tenant.ManagerEmail, NexoraApiFactory.Password));
        await login.AssertProblemAsync(HttpStatusCode.Unauthorized, "tenant_suspended");

        // Other tenants are unaffected.
        await (await (await factory.LoginAsync(A.ManagerEmail)).GetAsync("/api/v1/transactions")).EnsureStatusAsync(HttpStatusCode.OK);
    }
}
