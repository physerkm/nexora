using Microsoft.EntityFrameworkCore;
using Nexora.Application.Common;
using Nexora.Domain.Entities;
using Nexora.Infrastructure.Persistence;

namespace Nexora.UnitTests.Infrastructure;

/// <summary>
/// The tenant filter and write guards are verified against PostgreSQL in the integration tests; the EF in-memory
/// provider is enough to check the model-level behaviour quickly.
/// </summary>
public class DbContextGuardTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static NexoraDbContext Create(string database, RequestContext context) =>
        new(new DbContextOptionsBuilder<NexoraDbContext>().UseInMemoryDatabase(database).Options, context);

    private static RequestContext TenantUser(Guid tenantId)
    {
        var context = new RequestContext();
        context.SetUser(Guid.NewGuid(), "u@t.test", tenantId, null, ["TenantAdmin"]);
        return context;
    }

    [Fact]
    public async Task Query_filter_only_returns_rows_of_the_current_tenant()
    {
        var db = Guid.NewGuid().ToString();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var system = new RequestContext();
        system.UseSystemScope();
        await using (var seed = Create(db, system))
        {
            seed.Branches.AddRange(Branch.Create(tenantA, "A-1", "A one", null, Now), Branch.Create(tenantB, "B-1", "B one", null, Now));
            await seed.SaveChangesAsync();
        }

        await using var asA = Create(db, TenantUser(tenantA));
        Assert.Equal(["A-1"], await asA.Branches.Select(b => b.Code).ToListAsync());

        await using var anonymous = Create(db, new RequestContext());
        Assert.Empty(await anonymous.Branches.ToListAsync());

        await using var asSystem = Create(db, system);
        Assert.Equal(2, await asSystem.Branches.CountAsync());
    }

    [Fact]
    public async Task Writing_another_tenants_row_is_blocked()
    {
        await using var db = Create(Guid.NewGuid().ToString(), TenantUser(Guid.NewGuid()));
        db.Branches.Add(Branch.Create(Guid.NewGuid(), "X-1", "Foreign", null, Now));

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => db.SaveChangesAsync());
        Assert.Contains("Cross-tenant", ex.Message);
    }

    [Fact]
    public async Task Audit_logs_cannot_be_modified_or_deleted()
    {
        var system = new RequestContext();
        system.UseSystemScope();
        await using var db = Create(Guid.NewGuid().ToString(), system);

        var entry = AuditLog.Create(null, null, "system", "test", "Test", "1", null, null, null, Now);
        db.AuditLogs.Add(entry);
        await db.SaveChangesAsync();

        db.Entry(entry).Property(e => e.Action).CurrentValue = "tampered";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());

        db.Entry(entry).State = EntityState.Unchanged;
        db.AuditLogs.Remove(entry);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }
}
