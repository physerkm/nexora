using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexora.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Makes audit_logs append-only at the database level: any UPDATE, DELETE or TRUNCATE raises an error,
    /// regardless of which client or user issues it.
    /// </summary>
    public partial class AuditLogImmutability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION audit_logs_block_mutation() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'audit_logs is append-only: % is not allowed', TG_OP
                        USING ERRCODE = 'insufficient_privilege';
                END;
                $$;

                CREATE TRIGGER audit_logs_no_update_delete
                    BEFORE UPDATE OR DELETE ON audit_logs
                    FOR EACH ROW EXECUTE FUNCTION audit_logs_block_mutation();

                CREATE TRIGGER audit_logs_no_truncate
                    BEFORE TRUNCATE ON audit_logs
                    FOR EACH STATEMENT EXECUTE FUNCTION audit_logs_block_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS audit_logs_no_truncate ON audit_logs;
                DROP TRIGGER IF EXISTS audit_logs_no_update_delete ON audit_logs;
                DROP FUNCTION IF EXISTS audit_logs_block_mutation();
                """);
        }
    }
}
