using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Nexora.Domain.Common;
using Nexora.Domain.Entities;

namespace Nexora.Application.Abstractions;

/// <summary>
/// The EF Core context seen by the application layer. DbContext already is a unit of work with repository-like
/// DbSets, so services use it directly; provider specific or performance critical queries live in
/// Infrastructure repositories behind their own interfaces.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<Tenant> Tenants { get; }
    DbSet<Branch> Branches { get; }
    DbSet<Transaction> Transactions { get; }
    DbSet<FeeRule> FeeRules { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<OutboxMessage> OutboxMessages { get; }
    DbSet<IdempotencyRecord> IdempotencyRecords { get; }
    DbSet<AppUser> Users { get; }
    DbSet<IdentityRole<Guid>> Roles { get; }
    DbSet<IdentityUserRole<Guid>> UserRoles { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Runs <paramref name="work"/> inside a database transaction, honouring the configured retry strategy.</summary>
    Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken = default);

    /// <summary>Sets the concurrency token the client last saw, so a stale update fails with <c>ConcurrencyException</c>.</summary>
    void UseExpectedVersion<T>(T entity, uint expectedVersion) where T : class, IVersioned;
}
