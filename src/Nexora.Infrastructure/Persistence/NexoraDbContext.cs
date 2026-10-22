using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Nexora.Application.Abstractions;
using Nexora.Application.Common;
using Nexora.Domain.Common;
using Nexora.Domain.Entities;
using Npgsql;

namespace Nexora.Infrastructure.Persistence;

public sealed class NexoraDbContext(DbContextOptions<NexoraDbContext> options, ITenantContext tenant)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options), IApplicationDbContext
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<FeeRule> FeeRules => Set<FeeRule>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    // Evaluated per query: EF Core parameterises members of the context instance used in query filters.
    public Guid? CurrentTenantId => tenant.TenantId;
    public bool BypassTenantFilter => tenant.BypassTenantFilter;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasPostgresExtension("pg_trgm");
        builder.ApplyConfigurationsFromAssembly(typeof(NexoraDbContext).Assembly);

        // Tenant isolation. Fail-closed: with no tenant and no bypass, TenantId = NULL matches nothing.
        builder.Entity<Branch>().HasQueryFilter(e => BypassTenantFilter || e.TenantId == CurrentTenantId);
        builder.Entity<Transaction>().HasQueryFilter(e => BypassTenantFilter || e.TenantId == CurrentTenantId);
        builder.Entity<FeeRule>().HasQueryFilter(e => BypassTenantFilter || e.TenantId == CurrentTenantId);
        builder.Entity<AuditLog>().HasQueryFilter(e => BypassTenantFilter || e.TenantId == CurrentTenantId);
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        GuardInvariants();

        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyException(ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg)
        {
            throw new UniqueConstraintException(pg.ConstraintName, ex);
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardInvariants();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public async Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken = default)
    {
        if (Database.CurrentTransaction is not null)
        {
            return await work(cancellationToken);
        }

        var strategy = Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async ct =>
        {
            await using var transaction = await Database.BeginTransactionAsync(ct);
            var result = await work(ct);
            await transaction.CommitAsync(ct);
            return result;
        }, cancellationToken);
    }

    public void UseExpectedVersion<T>(T entity, uint expectedVersion) where T : class, IVersioned =>
        Entry(entity).Property(e => e.Version).OriginalValue = expectedVersion;

    /// <summary>
    /// Defence in depth on writes: audit rows are append-only, and tenant-owned rows can only be written
    /// for the tenant of the current request (system scope / cross-tenant platform views are exempt).
    /// </summary>
    private void GuardInvariants()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is AuditLog && entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException("Audit logs are append-only and cannot be modified or deleted.");
            }

            if (!BypassTenantFilter
                && entry.Entity is ITenantOwned owned
                && entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
                && owned.TenantId != CurrentTenantId)
            {
                throw new ForbiddenException("Cross-tenant write blocked.");
            }
        }
    }
}
