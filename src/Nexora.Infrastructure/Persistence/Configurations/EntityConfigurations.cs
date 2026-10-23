using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexora.Domain;
using Nexora.Domain.Common;
using Nexora.Domain.Entities;

namespace Nexora.Infrastructure.Persistence.Configurations;

internal static class ConfigurationExtensions
{
    /// <summary>Maps <see cref="IVersioned.Version"/> to PostgreSQL's xmin system column (no extra column, changes on every UPDATE).</summary>
    public static void HasXminVersion<T>(this EntityTypeBuilder<T> builder) where T : class, IVersioned =>
        builder.Property(e => e.Version)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();
}

internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> b)
    {
        b.ToTable("tenants");
        b.Property(t => t.Name).HasMaxLength(100).IsRequired();
        b.Property(t => t.Slug).HasMaxLength(50).IsRequired();
        b.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(t => t.WebhookUrl).HasMaxLength(500);
        b.Property(t => t.WebhookSecret).HasMaxLength(100);
        b.HasIndex(t => t.Slug).IsUnique().HasDatabaseName("ux_tenants_slug");
        b.HasXminVersion();
    }
}

internal sealed class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> b)
    {
        b.ToTable("branches");
        b.Property(x => x.Code).HasMaxLength(20).IsRequired();
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.City).HasMaxLength(100);
        b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasDatabaseName("ux_branches_tenant_code");
        b.HasXminVersion();
    }
}

internal sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> b)
    {
        b.ToTable("transactions");
        b.Property(x => x.Reference).HasMaxLength(32).IsRequired();
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Amount).HasPrecision(18, 2);
        b.Property(x => x.Fee).HasPrecision(18, 2);
        b.Property(x => x.NetAmount).HasPrecision(18, 2);
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
        b.Property(x => x.Description).HasMaxLength(500);
        b.Property(x => x.CounterpartyName).HasMaxLength(200);
        b.Property(x => x.StatusReason).HasMaxLength(Transaction.MaxReasonLength);
        b.Property(x => x.IdempotencyKey).HasMaxLength(100);

        b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);

        // Default listing: WHERE tenant_id = @t ORDER BY created_at DESC, id DESC  (also serves keyset pagination).
        // The included columns turn every dashboard aggregate over a date range into an index-only range scan
        // (90 days of a 12M row tenant: ~240k index entries instead of scattered heap pages).
        b.HasIndex(x => new { x.TenantId, x.CreatedAt, x.Id })
            .IsDescending(false, true, true)
            .IncludeProperties(x => new { x.Status, x.BranchId, x.Currency, x.Amount, x.Fee })
            .HasDatabaseName("ix_transactions_tenant_created");

        // Platform admins list every tenant at once: ORDER BY created_at DESC, id DESC without a tenant prefix.
        b.HasIndex(x => new { x.CreatedAt, x.Id })
            .IsDescending(true, true)
            .IsCreatedConcurrently()
            .HasDatabaseName("ix_transactions_created");

        // Status filtered listings and sorting by status (newest/oldest first within a status).
        b.HasIndex(x => new { x.TenantId, x.Status, x.CreatedAt, x.Id })
            .IsCreatedConcurrently()
            .HasDatabaseName("ix_transactions_tenant_status");

        // Sorting by amount (both directions via a backward scan).
        b.HasIndex(x => new { x.TenantId, x.Amount, x.Id })
            .IsCreatedConcurrently()
            .HasDatabaseName("ix_transactions_tenant_amount");

        b.HasIndex(x => new { x.TenantId, x.BranchId, x.CreatedAt })
            .IsDescending(false, false, true)
            .HasDatabaseName("ix_transactions_tenant_branch_created");

        b.HasIndex(x => new { x.TenantId, x.Reference }).IsUnique().HasDatabaseName("ux_transactions_tenant_reference");

        // Idempotency safety net: one transaction per (tenant, user, key).
        b.HasIndex(x => new { x.TenantId, x.CreatedByUserId, x.IdempotencyKey })
            .IsUnique()
            .HasFilter("idempotency_key IS NOT NULL")
            .HasDatabaseName("ux_transactions_idempotency_key");

        // Small partial index used only by the expiry job.
        b.HasIndex(x => x.CreatedAt)
            .HasFilter("status = 'Pending'")
            .HasDatabaseName("ix_transactions_pending_created");

        // Trigram indexes make ILIKE '%term%' searches index-assisted instead of full scans.
        b.HasIndex(x => x.Reference).HasMethod("gin").HasOperators("gin_trgm_ops").HasDatabaseName("ix_transactions_reference_trgm");
        b.HasIndex(x => x.CounterpartyName).HasMethod("gin").HasOperators("gin_trgm_ops").HasDatabaseName("ix_transactions_counterparty_trgm");
        b.HasIndex(x => x.Description).HasMethod("gin").HasOperators("gin_trgm_ops").HasDatabaseName("ix_transactions_description_trgm");

        b.HasXminVersion();
    }
}

internal sealed class FeeRuleConfiguration : IEntityTypeConfiguration<FeeRule>
{
    public void Configure(EntityTypeBuilder<FeeRule> b)
    {
        b.ToTable("fee_rules");
        b.Property(x => x.TransactionType).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
        b.Property(x => x.Percentage).HasPrecision(7, 4);
        b.Property(x => x.FixedAmount).HasPrecision(18, 2);
        b.Property(x => x.MinFee).HasPrecision(18, 2);
        b.Property(x => x.MaxFee).HasPrecision(18, 2);
        b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);

        // At most one active rule per (tenant, type, currency); NULL currency ("any") counts as a value.
        b.HasIndex(x => new { x.TenantId, x.TransactionType, x.Currency })
            .IsUnique()
            .AreNullsDistinct(false)
            .HasFilter("is_active")
            .HasDatabaseName("ux_fee_rules_active");

        b.HasXminVersion();
    }
}

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.ToTable("audit_logs");
        b.Property(x => x.Action).HasMaxLength(100).IsRequired();
        b.Property(x => x.EntityType).HasMaxLength(50).IsRequired();
        b.Property(x => x.EntityId).HasMaxLength(256).IsRequired();
        b.Property(x => x.UserEmail).HasMaxLength(256);
        b.Property(x => x.Data).HasColumnType("jsonb");
        b.Property(x => x.CorrelationId).HasMaxLength(100);
        b.Property(x => x.IpAddress).HasMaxLength(64);

        b.HasIndex(x => new { x.TenantId, x.CreatedAt }).IsDescending(false, true).HasDatabaseName("ix_audit_logs_tenant_created");
        b.HasIndex(x => new { x.EntityType, x.EntityId }).HasDatabaseName("ix_audit_logs_entity");
        b.HasIndex(x => x.UserId).HasDatabaseName("ix_audit_logs_user");
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("refresh_tokens");
        b.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.RevokedReason).HasMaxLength(50);
        b.Property(x => x.CreatedByIp).HasMaxLength(64);
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => x.TokenHash).IsUnique().HasDatabaseName("ux_refresh_tokens_hash");
        b.HasIndex(x => x.FamilyId).HasDatabaseName("ix_refresh_tokens_family");
        b.HasIndex(x => x.UserId).HasDatabaseName("ix_refresh_tokens_user");
        b.HasXminVersion();
    }
}

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> b)
    {
        b.ToTable("outbox_messages");
        b.Property(x => x.Type).HasMaxLength(100).IsRequired();
        b.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.LastError).HasMaxLength(2000);
        b.HasIndex(x => x.NextAttemptAt).HasFilter("status = 'Pending'").HasDatabaseName("ix_outbox_pending");
    }
}

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> b)
    {
        b.ToTable("idempotency_records");
        b.Property(x => x.Key).HasMaxLength(100).IsRequired();
        b.Property(x => x.RequestMethod).HasMaxLength(10).IsRequired();
        b.Property(x => x.RequestPath).HasMaxLength(500).IsRequired();
        b.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.ResponseBody).HasColumnType("jsonb");
        b.HasIndex(x => new { x.UserId, x.Key }).IsUnique().HasDatabaseName("ux_idempotency_user_key");
        b.HasIndex(x => x.ExpiresAt).HasDatabaseName("ix_idempotency_expires");
    }
}

internal sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> b)
    {
        b.ToTable("users");
        b.Property(x => x.FullName).HasMaxLength(100).IsRequired();
        b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.SetNull);
        b.HasIndex(x => x.TenantId).HasDatabaseName("ix_users_tenant");

        // Emails are globally unique so sign-in needs no tenant selector.
        b.HasIndex(x => x.NormalizedEmail).IsUnique().HasDatabaseName("ux_users_normalized_email");
    }
}

internal sealed class IdentityTablesConfiguration :
    IEntityTypeConfiguration<IdentityRole<Guid>>,
    IEntityTypeConfiguration<IdentityUserRole<Guid>>,
    IEntityTypeConfiguration<IdentityUserClaim<Guid>>,
    IEntityTypeConfiguration<IdentityUserLogin<Guid>>,
    IEntityTypeConfiguration<IdentityUserToken<Guid>>,
    IEntityTypeConfiguration<IdentityRoleClaim<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityRole<Guid>> b) => b.ToTable("roles");
    public void Configure(EntityTypeBuilder<IdentityUserRole<Guid>> b) => b.ToTable("user_roles");
    public void Configure(EntityTypeBuilder<IdentityUserClaim<Guid>> b) => b.ToTable("user_claims");
    public void Configure(EntityTypeBuilder<IdentityUserLogin<Guid>> b) => b.ToTable("user_logins");
    public void Configure(EntityTypeBuilder<IdentityUserToken<Guid>> b) => b.ToTable("user_tokens");
    public void Configure(EntityTypeBuilder<IdentityRoleClaim<Guid>> b) => b.ToTable("role_claims");
}
