using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PolvorApp.AuditPrivacy.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditRetentionAndGuard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_action_occurred_at_id",
                schema: "audit",
                table: "audit_entries",
                columns: new[] { "action", "occurred_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_actor_user_id_occurred_at_id",
                schema: "audit",
                table: "audit_entries",
                columns: new[] { "actor_user_id", "occurred_at", "id" },
                filter: "actor_user_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_comparsa_id_occurred_at_id",
                schema: "audit",
                table: "audit_entries",
                columns: new[] { "comparsa_id", "occurred_at", "id" },
                filter: "comparsa_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_entity_type_entity_id_occurred_at_id",
                schema: "audit",
                table: "audit_entries",
                columns: new[] { "entity_type", "entity_id", "occurred_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_occurred_at_id",
                schema: "audit",
                table: "audit_entries",
                columns: new[] { "occurred_at", "id" });

            migrationBuilder.DropIndex(
                name: "ix_audit_entries_actor_user_id_occurred_at",
                schema: "audit",
                table: "audit_entries");

            migrationBuilder.DropIndex(
                name: "ix_audit_entries_comparsa_id_occurred_at",
                schema: "audit",
                table: "audit_entries");

            migrationBuilder.DropIndex(
                name: "ix_audit_entries_entity_type_entity_id",
                schema: "audit",
                table: "audit_entries");

            migrationBuilder.DropIndex(
                name: "ix_audit_entries_occurred_at",
                schema: "audit",
                table: "audit_entries");

            // Design D9: user updates no longer record names; remove those already recorded. This runs
            // before the guard exists.
            migrationBuilder.Sql(
                """
                UPDATE audit.audit_entries
                SET data = (data #- '{previous,name}') #- '{current,name}'
                WHERE action = 'UserUpdated' AND data IS NOT NULL
                """);

            // Design D4: the database refuses changes to audit entries, except a delete in the retention
            // purge's transaction and a change of data alone in a GDPR redaction's transaction. The
            // comparison covers every column but data, including columns added later.
            migrationBuilder.Sql(
                """
                CREATE FUNCTION audit.guard_audit_entries() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE
                    mode text := current_setting('polvorapp.audit_maintenance', true);
                BEGIN
                    IF TG_OP = 'DELETE' AND mode = 'purge' THEN
                        RETURN OLD;
                    END IF;
                    IF TG_OP = 'UPDATE' AND mode = 'redact'
                       AND (to_jsonb(NEW) - 'data') IS NOT DISTINCT FROM (to_jsonb(OLD) - 'data') THEN
                        RETURN NEW;
                    END IF;
                    RAISE EXCEPTION 'audit entries are append-only: % refused', TG_OP USING ERRCODE = 'PA001';
                END
                $$;
                """);
            migrationBuilder.Sql(
                """
                CREATE TRIGGER guard_audit_entries BEFORE UPDATE OR DELETE ON audit.audit_entries
                FOR EACH ROW EXECUTE FUNCTION audit.guard_audit_entries();
                """);
            migrationBuilder.Sql(
                """
                CREATE FUNCTION audit.refuse_audit_truncate() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'audit entries are append-only: TRUNCATE refused' USING ERRCODE = 'PA001';
                END
                $$;
                """);
            migrationBuilder.Sql(
                """
                CREATE TRIGGER refuse_audit_truncate BEFORE TRUNCATE ON audit.audit_entries
                FOR EACH STATEMENT EXECUTE FUNCTION audit.refuse_audit_truncate();
                """);

            // Also fire under session_replication_role = replica.
            migrationBuilder.Sql("ALTER TABLE audit.audit_entries ENABLE ALWAYS TRIGGER guard_audit_entries;");
            migrationBuilder.Sql("ALTER TABLE audit.audit_entries ENABLE ALWAYS TRIGGER refuse_audit_truncate;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The removed names are not restored.
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS refuse_audit_truncate ON audit.audit_entries;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS audit.refuse_audit_truncate();");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS guard_audit_entries ON audit.audit_entries;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS audit.guard_audit_entries();");

            migrationBuilder.DropIndex(
                name: "ix_audit_entries_action_occurred_at_id",
                schema: "audit",
                table: "audit_entries");

            migrationBuilder.DropIndex(
                name: "ix_audit_entries_actor_user_id_occurred_at_id",
                schema: "audit",
                table: "audit_entries");

            migrationBuilder.DropIndex(
                name: "ix_audit_entries_comparsa_id_occurred_at_id",
                schema: "audit",
                table: "audit_entries");

            migrationBuilder.DropIndex(
                name: "ix_audit_entries_entity_type_entity_id_occurred_at_id",
                schema: "audit",
                table: "audit_entries");

            migrationBuilder.DropIndex(
                name: "ix_audit_entries_occurred_at_id",
                schema: "audit",
                table: "audit_entries");

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_actor_user_id_occurred_at",
                schema: "audit",
                table: "audit_entries",
                columns: new[] { "actor_user_id", "occurred_at" },
                filter: "actor_user_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_comparsa_id_occurred_at",
                schema: "audit",
                table: "audit_entries",
                columns: new[] { "comparsa_id", "occurred_at" },
                filter: "comparsa_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_entity_type_entity_id",
                schema: "audit",
                table: "audit_entries",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_occurred_at",
                schema: "audit",
                table: "audit_entries",
                column: "occurred_at");
        }
    }
}
