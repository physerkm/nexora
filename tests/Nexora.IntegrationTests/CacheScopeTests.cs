using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexora.Application.Common;
using Nexora.Application.DTOs;
using Nexora.Application.Services;
using Nexora.Domain;
using Nexora.IntegrationTests.Infrastructure;

namespace Nexora.IntegrationTests;

/// <summary>
/// HybridCache runs one factory for all concurrent callers of a key and finishes it for the others even after
/// the caller that started it has gone (found by the browser tests: a page reload aborted the first request,
/// its DbContext was disposed, and the shared lookup failed for the other request with ObjectDisposedException).
/// Simulated here by calling the services after their request scope was disposed.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CacheScopeTests(NexoraApiFactory factory)
{
    [Fact]
    public async Task Cached_lookups_still_work_after_the_calling_request_ended()
    {
        var tenant = factory.TenantA;
        var operatorUser = await factory.LoginAsync(tenant.FreeOperatorEmail);
        await operatorUser.CreateTransactionAsync(tenant.MainBranchId, 42m);

        var user = await factory.WithDbAsync(db => db.Users.AsNoTracking().SingleAsync(u => u.Email == tenant.ManagerEmail));

        SessionValidator sessions;
        TenantStatusProvider tenants;
        ReportService reports;
        await using (var request = factory.Services.CreateAsyncScope())
        {
            var sp = request.ServiceProvider;
            sp.GetRequiredService<RequestContext>().SetUser(user.Id, user.Email, user.TenantId, null, [Roles.Manager]);
            sessions = sp.GetRequiredService<SessionValidator>();
            tenants = sp.GetRequiredService<TenantStatusProvider>();
            reports = sp.GetRequiredService<ReportService>();
        }

        // Force cache misses so the factories really run.
        await sessions.InvalidateAsync(user.Id);
        await tenants.InvalidateAsync(tenant.Id);
        var from = DateTimeOffset.UtcNow.AddDays(-2).AddMinutes(-Random.Shared.Next(1, 10_000));
        var to = DateTimeOffset.UtcNow.AddDays(1);

        Assert.True(await sessions.IsValidAsync(user.Id, user.SecurityStamp, jti: null, CancellationToken.None));
        Assert.Equal(TenantStatus.Active, await tenants.GetStatusAsync(tenant.Id, CancellationToken.None));

        var report = await reports.GetSummaryAsync(new ReportSummaryQuery(from, to, null), CancellationToken.None);
        Assert.False(report.CacheHit);

        // The report's own scope keeps the caller's tenant: it counts tenant A's rows only.
        var fromMinute = new DateTimeOffset(from.Year, from.Month, from.Day, from.Hour, from.Minute, 0, TimeSpan.Zero);
        var toMinute = new DateTimeOffset(to.Year, to.Month, to.Day, to.Hour, to.Minute, 0, TimeSpan.Zero);
        var expected = await factory.WithDbAsync(db => db.Transactions.LongCountAsync(t =>
            t.TenantId == tenant.Id && t.CreatedAt >= fromMinute && t.CreatedAt < toMinute));
        Assert.True(expected > 0);
        Assert.Equal(expected, report.Summary.StatusBreakdown.Sum(s => s.Count));
    }
}
