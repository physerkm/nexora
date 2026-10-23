using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexora.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Indexes measured against a 24M row table (IBM TabFormer import):
    /// <list type="bullet">
    /// <item>amount and status sorting, and the platform-wide listing, no longer sort the whole table;</item>
    /// <item>the default listing index includes the report columns, so dashboard aggregates are index-only range scans.</item>
    /// </list>
    /// Every index is built CONCURRENTLY (outside a transaction) so writes keep flowing while it builds, and a
    /// replaced index is dropped only after its successor exists.
    /// </summary>
    public partial class TransactionListIndexes : Migration
    {
        private const string TenantCreatedColumns = "transactions (tenant_id, created_at DESC, id DESC)";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_transactions_created",
                table: "transactions",
                columns: new[] { "created_at", "id" },
                descending: new bool[0])
                .Annotation("Npgsql:CreatedConcurrently", true);

            migrationBuilder.CreateIndex(
                name: "ix_transactions_tenant_amount",
                table: "transactions",
                columns: new[] { "tenant_id", "amount", "id" })
                .Annotation("Npgsql:CreatedConcurrently", true);

            migrationBuilder.CreateIndex(
                name: "ix_transactions_tenant_status",
                table: "transactions",
                columns: new[] { "tenant_id", "status", "created_at", "id" })
                .Annotation("Npgsql:CreatedConcurrently", true);

            migrationBuilder.Sql("DROP INDEX CONCURRENTLY IF EXISTS ix_transactions_tenant_status_created;", suppressTransaction: true);

            ReplaceTenantCreated(migrationBuilder, " INCLUDE (status, branch_id, currency, amount, fee)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_transactions_tenant_status_created ON transactions (tenant_id, status, created_at DESC);",
                suppressTransaction: true);
            ReplaceTenantCreated(migrationBuilder, "");

            migrationBuilder.DropIndex(name: "ix_transactions_created", table: "transactions");
            migrationBuilder.DropIndex(name: "ix_transactions_tenant_amount", table: "transactions");
            migrationBuilder.DropIndex(name: "ix_transactions_tenant_status", table: "transactions");
        }

        // Build the new definition under a temporary name, swap it in, then rename: the listing never runs without an index.
        private static void ReplaceTenantCreated(MigrationBuilder migrationBuilder, string include)
        {
            migrationBuilder.Sql("DROP INDEX CONCURRENTLY IF EXISTS ix_transactions_tenant_created_new;", suppressTransaction: true);
            migrationBuilder.Sql($"CREATE INDEX CONCURRENTLY ix_transactions_tenant_created_new ON {TenantCreatedColumns}{include};", suppressTransaction: true);
            migrationBuilder.Sql("DROP INDEX CONCURRENTLY IF EXISTS ix_transactions_tenant_created;", suppressTransaction: true);
            migrationBuilder.Sql("ALTER INDEX ix_transactions_tenant_created_new RENAME TO ix_transactions_tenant_created;", suppressTransaction: true);
        }
    }
}
