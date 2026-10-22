using System.Net;
using Nexora.Application.DTOs;
using Nexora.Domain;
using Nexora.IntegrationTests.Infrastructure;

namespace Nexora.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class AuthorizationTests(NexoraApiFactory factory)
{
    private TestTenant Tenant => factory.TenantA;

    public static TheoryData<string, string> ProtectedEndpoints => new()
    {
        { "GET", "/api/v1/transactions" },
        { "POST", "/api/v1/transactions" },
        { "GET", "/api/v1/branches" },
        { "GET", "/api/v1/users" },
        { "GET", "/api/v1/fee-rules" },
        { "GET", "/api/v1/reports/summary" },
        { "GET", "/api/v1/audit-logs" },
        { "GET", "/api/v1/platform/tenants" },
        { "GET", "/api/v1/tenant" },
        { "GET", "/api/v1/auth/me" },
        { "POST", "/api/v1/auth/logout" },
    };

    [Theory]
    [MemberData(nameof(ProtectedEndpoints))]
    public async Task Anonymous_requests_get_401_problem_details(string method, string url)
    {
        var response = await factory.Anonymous().SendAsync(new HttpMethod(method), url);
        await response.AssertProblemAsync(HttpStatusCode.Unauthorized, "unauthorized");
    }

    [Theory]
    [InlineData("operator", "GET", "/api/v1/audit-logs")]
    [InlineData("operator", "GET", "/api/v1/users")]
    [InlineData("operator", "GET", "/api/v1/reports/summary")]
    [InlineData("operator", "GET", "/api/v1/platform/tenants")]
    [InlineData("operator", "GET", "/api/v1/tenant")]
    [InlineData("auditor", "POST", "/api/v1/branches")]
    [InlineData("auditor", "POST", "/api/v1/fees/quote")]
    [InlineData("manager", "POST", "/api/v1/users")]
    [InlineData("manager", "GET", "/api/v1/platform/tenants")]
    [InlineData("admin", "GET", "/api/v1/platform/tenants")]
    public async Task Roles_without_permission_get_403(string role, string method, string url)
    {
        var email = role switch
        {
            "operator" => Tenant.OperatorEmail,
            "auditor" => Tenant.AuditorEmail,
            "manager" => Tenant.ManagerEmail,
            _ => Tenant.AdminEmail,
        };

        var client = await factory.LoginAsync(email);
        var response = await client.SendAsync(new HttpMethod(method), url, method == "POST" ? new { } : null);
        await response.AssertProblemAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Operator_cannot_approve_and_auditor_cannot_create()
    {
        var operatorUser = await factory.LoginAsync(Tenant.OperatorEmail);
        var tx = await operatorUser.CreateTransactionAsync(Tenant.MainBranchId);

        await (await operatorUser.PostAsync($"/api/v1/transactions/{tx.Id}/approve", new ApproveTransactionRequest(tx.Version)))
            .AssertProblemAsync(HttpStatusCode.Forbidden);

        var auditor = await factory.LoginAsync(Tenant.AuditorEmail);
        var create = await auditor.PostAsync("/api/v1/transactions",
            new CreateTransactionRequest(Tenant.MainBranchId, TransactionType.Payment, 10m, "TRY", null, null), Guid.NewGuid().ToString());
        await create.AssertProblemAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Branch_bound_operator_is_limited_to_their_branch()
    {
        var operatorUser = await factory.LoginAsync(Tenant.OperatorEmail);
        var response = await operatorUser.PostAsync("/api/v1/transactions",
            new CreateTransactionRequest(Tenant.OtherBranchId, TransactionType.Payment, 10m, "TRY", null, null), Guid.NewGuid().ToString());
        await response.AssertProblemAsync(HttpStatusCode.Forbidden, "forbidden");

        // An operator without a branch binding can use any branch of the tenant.
        var free = await factory.LoginAsync(Tenant.FreeOperatorEmail);
        await free.CreateTransactionAsync(Tenant.OtherBranchId);
    }

    [Fact]
    public async Task Operator_can_cancel_only_their_own_pending_transaction()
    {
        var owner = await factory.LoginAsync(Tenant.FreeOperatorEmail);
        var other = await factory.LoginAsync(Tenant.OperatorEmail);
        var tx = await owner.CreateTransactionAsync(Tenant.MainBranchId);

        await (await other.PostAsync($"/api/v1/transactions/{tx.Id}/cancel", new TransactionReasonRequest(tx.Version, "not mine")))
            .AssertProblemAsync(HttpStatusCode.Forbidden);

        var cancelled = await owner.PostAsync($"/api/v1/transactions/{tx.Id}/cancel", new TransactionReasonRequest(tx.Version, "typo"));
        await cancelled.EnsureStatusAsync(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Only_approvers_can_cancel_an_approved_transaction()
    {
        var owner = await factory.LoginAsync(Tenant.FreeOperatorEmail);
        var manager = await factory.LoginAsync(Tenant.ManagerEmail);
        var tx = await owner.CreateTransactionAsync(Tenant.MainBranchId);
        var approved = await (await manager.PostAsync($"/api/v1/transactions/{tx.Id}/approve", new ApproveTransactionRequest(tx.Version)))
            .ReadAsync<TransactionDto>();

        await (await owner.PostAsync($"/api/v1/transactions/{tx.Id}/cancel", new TransactionReasonRequest(approved.Version, "undo")))
            .AssertProblemAsync(HttpStatusCode.Forbidden);

        var reversed = await manager.PostAsync($"/api/v1/transactions/{tx.Id}/cancel", new TransactionReasonRequest(approved.Version, "chargeback"));
        await reversed.EnsureStatusAsync(HttpStatusCode.OK);
        Assert.Equal(TransactionStatus.Cancelled, (await reversed.ReadAsync<TransactionDto>()).Status);
    }

    [Fact]
    public async Task Tenant_admin_cannot_create_platform_admins_or_lock_themselves_out()
    {
        var admin = await factory.LoginAsync(Tenant.AdminEmail);
        var escalate = await admin.PostAsync("/api/v1/users",
            new CreateUserRequest($"evil-{Guid.NewGuid():N}@x.test", "Evil", NexoraApiFactory.Password, Roles.PlatformAdmin, null));
        await escalate.AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");

        var self = await admin.PutAsync($"/api/v1/users/{admin.Session!.User.Id}", new UpdateUserRequest("Me", Roles.Auditor, null, true));
        await self.AssertProblemAsync(HttpStatusCode.Conflict, "self_lockout");
    }
}
