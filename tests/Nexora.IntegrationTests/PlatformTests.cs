using System.Net;
using Microsoft.EntityFrameworkCore;
using Nexora.Application.DTOs;
using Nexora.Domain;
using Nexora.Infrastructure.BackgroundJobs;
using Nexora.IntegrationTests.Infrastructure;
using Npgsql;

namespace Nexora.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class AuditImmutabilityTests(NexoraApiFactory factory)
{
    [Theory]
    [InlineData("UPDATE audit_logs SET action = 'tampered'")]
    [InlineData("DELETE FROM audit_logs")]
    [InlineData("TRUNCATE audit_logs")]
    public async Task Database_rejects_any_mutation_of_audit_rows(string sql)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Contains("append-only", ex.MessageText);
    }

    [Fact]
    public async Task There_is_no_api_to_change_audit_logs()
    {
        var admin = await factory.LoginAsync(factory.TenantA.AdminEmail);
        var page = await admin.GetJsonAsync<Nexora.Application.Common.PagedResult<AuditLogDto>>("/api/v1/audit-logs?pageSize=1");
        var id = page.Items[0].Id;

        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await admin.SendAsync(HttpMethod.Delete, $"/api/v1/audit-logs")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.SendAsync(HttpMethod.Delete, $"/api/v1/audit-logs/{id}")).StatusCode);
    }
}

[Collection(ApiCollection.Name)]
public sealed class PlatformAndOperationsTests(NexoraApiFactory factory)
{
    [Fact]
    public async Task Duplicate_tenant_slug_or_admin_email_is_a_conflict()
    {
        var platform = await factory.LoginAsync(NexoraApiFactory.PlatformAdminEmail);
        var a = factory.TenantA;

        var slug = await platform.PostAsync("/api/v1/platform/tenants",
            new CreateTenantRequest("Dup", a.Slug, $"new-{Guid.NewGuid():N}@x.test", "Dup Admin", NexoraApiFactory.Password));
        await slug.AssertProblemAsync(HttpStatusCode.Conflict, "slug_taken");

        var email = await platform.PostAsync("/api/v1/platform/tenants",
            new CreateTenantRequest("Dup", $"dup-{Guid.NewGuid():N}"[..20], a.AdminEmail, "Dup Admin", NexoraApiFactory.Password));
        await email.AssertProblemAsync(HttpStatusCode.Conflict, "email_taken");
    }

    [Fact]
    public async Task Tenant_admin_can_configure_a_signed_webhook()
    {
        var admin = await factory.LoginAsync(factory.TenantB.AdminEmail);
        var current = await admin.GetJsonAsync<TenantSettingsDto>("/api/v1/tenant");

        var insecure = await admin.PutAsync("/api/v1/tenant", new UpdateTenantSettingsRequest(current.Name, "http://hooks.example.com", false, current.Version));
        await insecure.AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");

        var updated = await (await admin.PutAsync("/api/v1/tenant", new UpdateTenantSettingsRequest(current.Name, "https://hooks.example.com/nexora", false, current.Version)))
            .ReadAsync<TenantSettingsDto>();
        Assert.StartsWith("whsec_", updated.WebhookSecret);

        var stale = await admin.PutAsync("/api/v1/tenant", new UpdateTenantSettingsRequest(current.Name, null, false, current.Version));
        await stale.AssertProblemAsync(HttpStatusCode.Conflict, "concurrency_conflict");

        await (await admin.PutAsync("/api/v1/tenant", new UpdateTenantSettingsRequest(current.Name, null, false, updated.Version))).EnsureStatusAsync(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Outbox_delivers_events_and_retries_failures_with_backoff()
    {
        var tenant = await TestTenant.CreateAsync(factory, "outbox");
        var operatorUser = await factory.LoginAsync(tenant.FreeOperatorEmail);
        var tx = await operatorUser.CreateTransactionAsync(tenant.MainBranchId);

        var dispatcher = factory.Services.GetRequiredService<OutboxDispatcher>();
        while (await dispatcher.DispatchPendingAsync(CancellationToken.None) > 0)
        {
        }

        var delivered = await factory.WithDbAsync(db => db.OutboxMessages.SingleAsync(m => m.TenantId == tenant.Id && m.Type == "transaction.created"));
        Assert.Equal(OutboxStatus.Processed, delivered.Status);

        // Point the tenant at an endpoint that refuses connections: delivery fails and is rescheduled.
        await factory.WithDbAsync(db => db.Tenants.Where(t => t.Id == tenant.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.WebhookUrl, "https://127.0.0.1:1/hook").SetProperty(t => t.WebhookSecret, "whsec_test")));

        var manager = await factory.LoginAsync(tenant.ManagerEmail);
        await (await manager.PostAsync($"/api/v1/transactions/{tx.Id}/approve", new ApproveTransactionRequest(tx.Version))).EnsureStatusAsync(HttpStatusCode.OK);
        await dispatcher.DispatchPendingAsync(CancellationToken.None);

        var failed = await factory.WithDbAsync(db => db.OutboxMessages.SingleAsync(m => m.TenantId == tenant.Id && m.Type == "transaction.approved"));
        Assert.Equal(OutboxStatus.Pending, failed.Status);
        Assert.Equal(1, failed.Attempts);
        Assert.NotNull(failed.LastError);
        Assert.True(failed.NextAttemptAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Maintenance_runs_under_an_advisory_lock()
    {
        var runner = factory.Services.GetRequiredService<MaintenanceRunner>();
        var results = await Task.WhenAll(runner.RunAsync(CancellationToken.None), runner.RunAsync(CancellationToken.None));
        Assert.Contains(results, r => r is not null);
    }

    [Fact]
    public async Task Health_endpoints_report_dependencies()
    {
        var client = factory.Anonymous();
        await (await client.GetAsync("/health/live")).EnsureStatusAsync(HttpStatusCode.OK);

        var ready = await client.GetAsync("/health/ready");
        await ready.EnsureStatusAsync(HttpStatusCode.OK);
        var body = await ready.Content.ReadAsStringAsync();
        Assert.Contains("\"database\"", body);
        Assert.Contains("Healthy", body);
    }

    [Fact]
    public async Task Correlation_id_is_echoed_and_included_in_errors()
    {
        var client = factory.Anonymous();
        var response = await client.SendAsync(HttpMethod.Get, "/api/v1/transactions", headers: new Dictionary<string, string> { ["X-Correlation-ID"] = "test-corr-123" });

        Assert.Equal("test-corr-123", response.Headers.GetValues("X-Correlation-ID").Single());
        Assert.Contains("test-corr-123", await response.Content.ReadAsStringAsync());

        var generated = await client.GetAsync("/health/live");
        Assert.Matches("^[0-9a-f]{32}$", generated.Headers.GetValues("X-Correlation-ID").Single());
    }

    [Fact]
    public async Task OpenApi_document_describes_security_and_idempotency()
    {
        var doc = await factory.Anonymous().Http.GetStringAsync("/openapi/v1.json");
        Assert.Contains("\"Bearer\"", doc);
        Assert.Contains("Idempotency-Key", doc);
        Assert.Contains("/api/v1/transactions/{id}/approve", doc);
    }
}
