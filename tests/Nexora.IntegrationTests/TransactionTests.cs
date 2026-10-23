using System.Net;
using Microsoft.EntityFrameworkCore;
using Nexora.Application.Common;
using Nexora.Application.DTOs;
using Nexora.Domain;
using Nexora.IntegrationTests.Infrastructure;

namespace Nexora.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class TransactionLifecycleTests(NexoraApiFactory factory)
{
    private TestTenant Tenant => factory.TenantA;

    [Fact]
    public async Task Create_approve_then_reverse_writes_a_complete_audit_trail()
    {
        var operatorUser = await factory.LoginAsync(Tenant.OperatorEmail);
        var manager = await factory.LoginAsync(Tenant.ManagerEmail);

        var created = await operatorUser.CreateTransactionAsync(Tenant.MainBranchId, 2500m);
        Assert.Equal(TransactionStatus.Pending, created.Status);
        Assert.StartsWith("TRX-", created.Reference);
        Assert.Contains(TransactionStatus.Approved, created.AllowedTransitions);

        var approved = await (await manager.PostAsync($"/api/v1/transactions/{created.Id}/approve", new ApproveTransactionRequest(created.Version)))
            .ReadAsync<TransactionDto>();
        Assert.Equal(TransactionStatus.Approved, approved.Status);
        Assert.Equal(manager.Session!.User.Id, approved.ReviewedByUserId);
        Assert.NotEqual(created.Version, approved.Version);
        Assert.Equal([TransactionStatus.Cancelled], approved.AllowedTransitions);

        var cancelled = await (await manager.PostAsync($"/api/v1/transactions/{created.Id}/cancel", new TransactionReasonRequest(approved.Version, "chargeback")))
            .ReadAsync<TransactionDto>();
        Assert.Equal(TransactionStatus.Cancelled, cancelled.Status);
        Assert.Equal("chargeback", cancelled.StatusReason);

        var history = await manager.GetJsonAsync<List<AuditLogDto>>($"/api/v1/transactions/{created.Id}/history");
        Assert.Equal(["transaction.created", "transaction.approved", "transaction.cancelled"], history.Select(h => h.Action));
        Assert.All(history, h => Assert.False(string.IsNullOrEmpty(h.CorrelationId)));

        var messages = await factory.WithDbAsync(db => db.OutboxMessages.Where(m => m.TenantId == Tenant.Id).ToListAsync());
        var events = messages.Where(m => m.Payload.Contains(created.Id.ToString())).Select(m => m.Type).Order();
        Assert.Equal(["transaction.approved", "transaction.cancelled", "transaction.created"], events);
    }

    [Fact]
    public async Task Fee_is_calculated_from_the_most_specific_active_rule()
    {
        var admin = await factory.LoginAsync(Tenant.AdminEmail);
        await (await admin.PostAsync("/api/v1/fee-rules", new CreateFeeRuleRequest(TransactionType.Payout, null, 1m, 0m, 0m, null)))
            .EnsureStatusAsync(HttpStatusCode.Created);
        await (await admin.PostAsync("/api/v1/fee-rules", new CreateFeeRuleRequest(TransactionType.Payout, "EUR", 2m, 1m, 0m, 50m)))
            .EnsureStatusAsync(HttpStatusCode.Created);

        var duplicate = await admin.PostAsync("/api/v1/fee-rules", new CreateFeeRuleRequest(TransactionType.Payout, null, 3m, 0m, 0m, null));
        await duplicate.AssertProblemAsync(HttpStatusCode.Conflict, "fee_rule_exists");

        var operatorUser = await factory.LoginAsync(Tenant.FreeOperatorEmail);
        var tryTx = await operatorUser.CreateTransactionAsync(Tenant.MainBranchId, 1000m, "TRY", TransactionType.Payout);
        var eurTx = await operatorUser.CreateTransactionAsync(Tenant.MainBranchId, 1000m, "EUR", TransactionType.Payout);
        var capped = await operatorUser.CreateTransactionAsync(Tenant.MainBranchId, 100000m, "EUR", TransactionType.Payout);

        Assert.Equal(10m, tryTx.Fee);
        Assert.Equal(21m, eurTx.Fee);
        Assert.Equal(979m, eurTx.NetAmount);
        Assert.Equal(50m, capped.Fee);

        var quote = await (await operatorUser.PostAsync("/api/v1/fees/quote", new FeeQuoteRequest(TransactionType.Payout, 1000m, "EUR"))).ReadAsync<FeeQuoteDto>();
        Assert.Equal(21m, quote.Fee);
    }

    [Fact]
    public async Task Creator_cannot_approve_their_own_transaction()
    {
        var manager = await factory.LoginAsync(Tenant.ManagerEmail);
        var tx = await manager.CreateTransactionAsync(Tenant.MainBranchId);

        var response = await manager.PostAsync($"/api/v1/transactions/{tx.Id}/approve", new ApproveTransactionRequest(tx.Version));
        await response.AssertProblemAsync(HttpStatusCode.UnprocessableEntity, "four_eyes_violation");

        var other = await factory.LoginAsync(Tenant.Manager2Email);
        await (await other.PostAsync($"/api/v1/transactions/{tx.Id}/approve", new ApproveTransactionRequest(tx.Version))).EnsureStatusAsync(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Invalid_transitions_are_rejected_with_409()
    {
        var operatorUser = await factory.LoginAsync(Tenant.OperatorEmail);
        var manager = await factory.LoginAsync(Tenant.ManagerEmail);
        var tx = await operatorUser.CreateTransactionAsync(Tenant.MainBranchId);

        var rejected = await (await manager.PostAsync($"/api/v1/transactions/{tx.Id}/reject", new TransactionReasonRequest(tx.Version, "missing KYC")))
            .ReadAsync<TransactionDto>();
        Assert.Equal(TransactionStatus.Rejected, rejected.Status);
        Assert.Empty(rejected.AllowedTransitions);

        await (await manager.PostAsync($"/api/v1/transactions/{tx.Id}/approve", new ApproveTransactionRequest(rejected.Version)))
            .AssertProblemAsync(HttpStatusCode.Conflict, "invalid_state_transition");
        await (await manager.PostAsync($"/api/v1/transactions/{tx.Id}/cancel", new TransactionReasonRequest(rejected.Version, "x")))
            .AssertProblemAsync(HttpStatusCode.Conflict, "invalid_state_transition");
    }

    [Fact]
    public async Task Reject_requires_a_reason()
    {
        var operatorUser = await factory.LoginAsync(Tenant.OperatorEmail);
        var manager = await factory.LoginAsync(Tenant.ManagerEmail);
        var tx = await operatorUser.CreateTransactionAsync(Tenant.MainBranchId);

        await (await manager.PostAsync($"/api/v1/transactions/{tx.Id}/reject", new TransactionReasonRequest(tx.Version, "")))
            .AssertProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task Stale_version_is_rejected_by_optimistic_concurrency()
    {
        var operatorUser = await factory.LoginAsync(Tenant.OperatorEmail);
        var manager = await factory.LoginAsync(Tenant.ManagerEmail);
        var tx = await operatorUser.CreateTransactionAsync(Tenant.MainBranchId);

        await (await manager.PostAsync($"/api/v1/transactions/{tx.Id}/approve", new ApproveTransactionRequest(tx.Version + 1000)))
            .AssertProblemAsync(HttpStatusCode.Conflict, "concurrency_conflict");

        var current = await manager.GetJsonAsync<TransactionDto>($"/api/v1/transactions/{tx.Id}");
        Assert.Equal(TransactionStatus.Pending, current.Status);
    }

    [Fact]
    public async Task Concurrent_reviews_of_the_same_version_let_exactly_one_win()
    {
        var operatorUser = await factory.LoginAsync(Tenant.OperatorEmail);
        var manager1 = await factory.LoginAsync(Tenant.ManagerEmail);
        var manager2 = await factory.LoginAsync(Tenant.Manager2Email);
        var tx = await operatorUser.CreateTransactionAsync(Tenant.MainBranchId);

        var responses = await Task.WhenAll(
            manager1.PostAsync($"/api/v1/transactions/{tx.Id}/approve", new ApproveTransactionRequest(tx.Version)),
            manager2.PostAsync($"/api/v1/transactions/{tx.Id}/reject", new TransactionReasonRequest(tx.Version, "race")));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);

        var reviewEvents = await factory.WithDbAsync(db => db.AuditLogs.CountAsync(a =>
            a.EntityId == tx.Id.ToString() && (a.Action == "transaction.approved" || a.Action == "transaction.rejected")));
        Assert.Equal(1, reviewEvents);
    }

    [Fact]
    public async Task Inactive_branch_cannot_receive_transactions()
    {
        var admin = await factory.LoginAsync(Tenant.AdminEmail);
        var created = await (await admin.PostAsync("/api/v1/branches", new CreateBranchRequest($"C{Guid.NewGuid():N}"[..8], "Closing", null)))
            .ReadAsync<BranchDto>();
        await (await admin.PutAsync($"/api/v1/branches/{created.Id}", new UpdateBranchRequest("Closed", null, false, created.Version)))
            .EnsureStatusAsync(HttpStatusCode.OK);

        var response = await admin.PostAsync("/api/v1/transactions",
            new CreateTransactionRequest(created.Id, TransactionType.Payment, 10m, "TRY", null, null), Guid.NewGuid().ToString());
        await response.AssertProblemAsync(HttpStatusCode.UnprocessableEntity, "branch_inactive");
    }

    [Fact]
    public async Task Stale_pending_transactions_are_expired_by_the_background_job()
    {
        var operatorUser = await factory.LoginAsync(Tenant.OperatorEmail);
        var tx = await operatorUser.CreateTransactionAsync(Tenant.MainBranchId);

        var expired = await factory.WithScopeAsync(sp =>
            sp.GetRequiredService<Nexora.Application.Services.TransactionService>().ExpireStaleAsync(TimeSpan.Zero, 10_000, CancellationToken.None));
        Assert.True(expired >= 1);

        var manager = await factory.LoginAsync(Tenant.ManagerEmail);
        var reloaded = await manager.GetJsonAsync<TransactionDto>($"/api/v1/transactions/{tx.Id}");
        Assert.Equal(TransactionStatus.Expired, reloaded.Status);

        var history = await manager.GetJsonAsync<List<AuditLogDto>>($"/api/v1/transactions/{tx.Id}/history");
        Assert.Equal("system", history[^1].UserEmail);
    }
}

[Collection(ApiCollection.Name)]
public sealed class IdempotencyTests(NexoraApiFactory factory)
{
    private TestTenant Tenant => factory.TenantA;

    private CreateTransactionRequest Request(decimal amount = 75m) =>
        new(Tenant.MainBranchId, TransactionType.Payment, amount, "TRY", "idempotency", "Retry Ltd");

    [Fact]
    public async Task Retrying_with_the_same_key_replays_the_original_response()
    {
        var client = await factory.LoginAsync(Tenant.OperatorEmail);
        var key = Guid.NewGuid().ToString();

        var first = await client.PostAsync("/api/v1/transactions", Request(), key);
        await first.EnsureStatusAsync(HttpStatusCode.Created);
        var original = await first.ReadAsync<TransactionDto>();

        var retry = await client.PostAsync("/api/v1/transactions", Request(), key);
        await retry.EnsureStatusAsync(HttpStatusCode.Created);
        Assert.Equal("true", retry.Headers.GetValues("Idempotent-Replayed").Single());
        Assert.Equal(original.Id, (await retry.ReadAsync<TransactionDto>()).Id);

        var rows = await factory.WithDbAsync(db => db.Transactions.CountAsync(t => t.IdempotencyKey == key));
        Assert.Equal(1, rows);
    }

    [Fact]
    public async Task Reusing_a_key_with_a_different_payload_is_rejected()
    {
        var client = await factory.LoginAsync(Tenant.OperatorEmail);
        var key = Guid.NewGuid().ToString();

        await (await client.PostAsync("/api/v1/transactions", Request(75m), key)).EnsureStatusAsync(HttpStatusCode.Created);
        var conflicting = await client.PostAsync("/api/v1/transactions", Request(76m), key);
        await conflicting.AssertProblemAsync(HttpStatusCode.UnprocessableEntity, "idempotency_key_reused");
    }

    [Fact]
    public async Task Key_is_required_and_validated()
    {
        var client = await factory.LoginAsync(Tenant.OperatorEmail);
        await (await client.PostAsync("/api/v1/transactions", Request())).AssertProblemAsync(HttpStatusCode.BadRequest, "idempotency_key_required");
        await (await client.PostAsync("/api/v1/transactions", Request(), "bad key with spaces")).AssertProblemAsync(HttpStatusCode.BadRequest, "idempotency_key_invalid");
    }

    [Fact]
    public async Task Keys_are_scoped_per_user()
    {
        var key = Guid.NewGuid().ToString();
        var a = await factory.LoginAsync(Tenant.OperatorEmail);
        var b = await factory.LoginAsync(Tenant.FreeOperatorEmail);

        var first = await (await a.PostAsync("/api/v1/transactions", Request(), key)).ReadAsync<TransactionDto>();
        var second = await b.PostAsync("/api/v1/transactions", Request(), key);
        await second.EnsureStatusAsync(HttpStatusCode.Created);
        Assert.NotEqual(first.Id, (await second.ReadAsync<TransactionDto>()).Id);
    }

    [Fact]
    public async Task Parallel_duplicates_create_exactly_one_transaction()
    {
        var client = await factory.LoginAsync(Tenant.OperatorEmail);
        var key = Guid.NewGuid().ToString();

        var responses = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => client.PostAsync("/api/v1/transactions", Request(), key)));

        Assert.All(responses, r => Assert.True(
            r.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK or HttpStatusCode.Conflict,
            $"unexpected {(int)r.StatusCode}"));
        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.Created);

        var ids = new HashSet<Guid>();
        foreach (var response in responses.Where(r => r.IsSuccessStatusCode))
        {
            ids.Add((await response.ReadAsync<TransactionDto>()).Id);
        }

        Assert.Single(ids);
        Assert.Equal(1, await factory.WithDbAsync(db => db.Transactions.CountAsync(t => t.IdempotencyKey == key)));
    }

    [Fact]
    public async Task Failed_requests_release_the_key_so_the_client_can_retry()
    {
        var admin = await factory.LoginAsync(Tenant.AdminEmail);
        var branch = await (await admin.PostAsync("/api/v1/branches", new CreateBranchRequest($"R{Guid.NewGuid():N}"[..8], "Retry", null)))
            .ReadAsync<BranchDto>();
        await (await admin.PutAsync($"/api/v1/branches/{branch.Id}", new UpdateBranchRequest("Retry", null, false, branch.Version))).EnsureStatusAsync(HttpStatusCode.OK);

        var key = Guid.NewGuid().ToString();
        var request = new CreateTransactionRequest(branch.Id, TransactionType.Payment, 10m, "TRY", null, null);
        await (await admin.PostAsync("/api/v1/transactions", request, key)).AssertProblemAsync(HttpStatusCode.UnprocessableEntity, "branch_inactive");

        var reloaded = await admin.GetJsonAsync<BranchDto>($"/api/v1/branches/{branch.Id}");
        await (await admin.PutAsync($"/api/v1/branches/{branch.Id}", new UpdateBranchRequest("Retry", null, true, reloaded.Version))).EnsureStatusAsync(HttpStatusCode.OK);

        await (await admin.PostAsync("/api/v1/transactions", request, key)).EnsureStatusAsync(HttpStatusCode.Created);
    }
}

[Collection(ApiCollection.Name)]
public sealed class TransactionQueryTests(NexoraApiFactory factory)
{
    [Fact]
    public async Task Filtering_sorting_and_offset_paging()
    {
        var tenant = await TestTenant.CreateAsync(factory, "query");
        var operatorUser = await factory.LoginAsync(tenant.FreeOperatorEmail);
        var amounts = new[] { 10m, 250m, 99.99m, 5000m, 42m, 1234.56m, 7m };
        foreach (var amount in amounts)
        {
            await operatorUser.CreateTransactionAsync(amount > 100 ? tenant.MainBranchId : tenant.OtherBranchId, amount,
                amount > 1000 ? "USD" : "TRY", description: FormattableString.Invariant($"Invoice {amount}"));
        }

        var manager = await factory.LoginAsync(tenant.ManagerEmail);

        var page1 = await manager.GetJsonAsync<PagedResult<TransactionDto>>("/api/v1/transactions?sortBy=amount&sortDir=desc&pageSize=3");
        Assert.Equal(7, page1.TotalCount);
        Assert.Equal(3, page1.TotalPages);
        Assert.Equal([5000m, 1234.56m, 250m], page1.Items.Select(t => t.Amount));

        var page3 = await manager.GetJsonAsync<PagedResult<TransactionDto>>("/api/v1/transactions?sortBy=amount&sortDir=desc&pageSize=3&page=3");
        Assert.Equal([7m], page3.Items.Select(t => t.Amount));

        var usd = await manager.GetJsonAsync<PagedResult<TransactionDto>>("/api/v1/transactions?currency=USD");
        Assert.Equal(2, usd.TotalCount);

        var range = await manager.GetJsonAsync<PagedResult<TransactionDto>>("/api/v1/transactions?minAmount=40&maxAmount=300");
        Assert.Equal([42m, 99.99m, 250m], range.Items.Select(t => t.Amount).Order());

        var branch = await manager.GetJsonAsync<PagedResult<TransactionDto>>($"/api/v1/transactions?branchId={tenant.OtherBranchId}");
        Assert.All(branch.Items, t => Assert.Equal(tenant.OtherBranchId, t.BranchId));
        Assert.Equal(4, branch.TotalCount);

        var search = await manager.GetJsonAsync<PagedResult<TransactionDto>>("/api/v1/transactions?search=invoice%2099.99");
        Assert.Equal(99.99m, Assert.Single(search.Items).Amount);

        var wildcard = await manager.GetJsonAsync<PagedResult<TransactionDto>>("/api/v1/transactions?search=%25");
        Assert.Empty(wildcard.Items);
    }

    [Fact]
    public async Task Cursor_paging_walks_every_row_exactly_once()
    {
        var tenant = await TestTenant.CreateAsync(factory, "cursor");
        var operatorUser = await factory.LoginAsync(tenant.FreeOperatorEmail);
        for (var i = 0; i < 11; i++)
        {
            await operatorUser.CreateTransactionAsync(tenant.MainBranchId, 10m + i);
        }

        var manager = await factory.LoginAsync(tenant.ManagerEmail);
        var seen = new List<Guid>();
        string? cursor = null;
        do
        {
            var url = "/api/v1/transactions?paging=cursor&pageSize=4" + (cursor is null ? "" : $"&cursor={cursor}");
            var page = await manager.GetJsonAsync<PagedResult<TransactionDto>>(url);
            Assert.Null(page.TotalCount);
            seen.AddRange(page.Items.Select(t => t.Id));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        Assert.Equal(11, seen.Count);
        Assert.Equal(11, seen.Distinct().Count());
    }

    [Fact]
    public async Task Total_count_is_capped_on_large_tenants()
    {
        var tenant = await TestTenant.CreateAsync(factory, "bigcount");
        var operatorUser = await factory.LoginAsync(tenant.FreeOperatorEmail);
        var seed = await operatorUser.CreateTransactionAsync(tenant.MainBranchId, 10m);

        // 10,050 more copies of the seed row, so the tenant holds more rows than the count cap. They are
        // inserted as Approved: thousands of old Pending rows would crowd the expiry job test's batch.
        await factory.WithDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO transactions (id, tenant_id, branch_id, reference, type, amount, currency, fee, net_amount, status,
                                      created_by_user_id, created_at)
            SELECT gen_random_uuid(), tenant_id, branch_id, reference || '-' || g, type, amount + g, currency, fee,
                   net_amount + g, 'Approved', created_by_user_id, created_at - make_interval(secs => g)
            FROM transactions, generate_series(1, 10050) AS g
            WHERE id = {seed.Id}
            """));

        var manager = await factory.LoginAsync(tenant.ManagerEmail);

        var all = await manager.GetJsonAsync<PagedResult<TransactionDto>>("/api/v1/transactions?pageSize=25");
        Assert.Equal(10_000, all.TotalCount);
        Assert.True(all.TotalCountCapped);
        Assert.Equal(400, all.TotalPages);

        var byAmount = await manager.GetJsonAsync<PagedResult<TransactionDto>>("/api/v1/transactions?sortBy=amount&sortDir=desc&pageSize=2");
        Assert.Equal([10_060m, 10_059m], byAmount.Items.Select(t => t.Amount));

        var filtered = await manager.GetJsonAsync<PagedResult<TransactionDto>>("/api/v1/transactions?minAmount=10000");
        Assert.Equal(61, filtered.TotalCount);
        Assert.False(filtered.TotalCountCapped);
    }

    [Theory]
    [InlineData("sortBy=passwordHash")]
    [InlineData("sortBy=type")]
    [InlineData("sortDir=sideways")]
    [InlineData("paging=cursor&sortBy=amount")]
    [InlineData("cursor=garbage")]
    [InlineData("status=NotAStatus")]
    public async Task Invalid_query_parameters_return_400(string query)
    {
        var manager = await factory.LoginAsync(factory.TenantA.ManagerEmail);
        var response = await manager.GetAsync($"/api/v1/transactions?{query}");
        await response.EnsureStatusAsync(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Report_summary_is_cached_and_invalidated_on_change()
    {
        var tenant = await TestTenant.CreateAsync(factory, "report");
        var operatorUser = await factory.LoginAsync(tenant.FreeOperatorEmail);
        var manager = await factory.LoginAsync(tenant.ManagerEmail);

        await operatorUser.CreateTransactionAsync(tenant.MainBranchId, 100m);

        // Fixed window so the cache key cannot change between calls.
        var url = $"/api/v1/reports/summary?from={DateTime.UtcNow.AddDays(-1):yyyy-MM-ddTHH:mm:00Z}&to={DateTime.UtcNow.AddDays(1):yyyy-MM-ddTHH:mm:00Z}";
        var first = await manager.GetAsync(url);
        Assert.Equal("MISS", first.Headers.GetValues("X-Cache").Single());
        Assert.Equal(1, (await first.ReadAsync<ReportSummaryDto>()).StatusBreakdown.Sum(s => s.Count));

        var second = await manager.GetAsync(url);
        Assert.Equal("HIT", second.Headers.GetValues("X-Cache").Single());

        var tx = await operatorUser.CreateTransactionAsync(tenant.MainBranchId, 50m, "USD");
        await (await manager.PostAsync($"/api/v1/transactions/{tx.Id}/approve", new ApproveTransactionRequest(tx.Version))).EnsureStatusAsync(HttpStatusCode.OK);

        var third = await manager.GetAsync(url);
        Assert.Equal("MISS", third.Headers.GetValues("X-Cache").Single());
        var summary = await third.ReadAsync<ReportSummaryDto>();
        Assert.Equal(2, summary.StatusBreakdown.Sum(s => s.Count));
        var usd = Assert.Single(summary.Totals, t => t.Currency == "USD");
        Assert.Equal(50m, usd.ApprovedVolume);
        Assert.Equal(2, summary.Daily.Sum(d => d.Count));
    }

    [Fact]
    public async Task Csv_export_streams_matching_rows()
    {
        var operatorUser = await factory.LoginAsync(factory.TenantA.FreeOperatorEmail);
        await operatorUser.CreateTransactionAsync(factory.TenantA.MainBranchId, description: "=HYPERLINK(\"http://evil\")");

        var auditor = await factory.LoginAsync(factory.TenantA.AuditorEmail);
        var response = await auditor.GetAsync("/api/v1/reports/transactions.csv");
        await response.EnsureStatusAsync(HttpStatusCode.OK);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);

        var lines = (await response.Content.ReadAsStringAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("reference,created_at", lines[0].TrimStart((char)0xFEFF));
        Assert.True(lines.Length > 1);
    }
}
