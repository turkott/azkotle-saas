using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AzKotle.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddAuditLog : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "audit_log",
            schema: "public",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                target_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                target_id = table.Column<Guid>(type: "uuid", nullable: true),
                ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                user_agent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                metadata = table.Column<string>(type: "jsonb", nullable: true),
                occurred_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_audit_log", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_audit_log_action",
            schema: "public",
            table: "audit_log",
            column: "action");

        migrationBuilder.CreateIndex(
            name: "IX_audit_log_occurred_at",
            schema: "public",
            table: "audit_log",
            column: "occurred_at");

        migrationBuilder.CreateIndex(
            name: "IX_audit_log_target_type_target_id",
            schema: "public",
            table: "audit_log",
            columns: new[] { "target_type", "target_id" });

        migrationBuilder.CreateIndex(
            name: "IX_audit_log_tenant_id",
            schema: "public",
            table: "audit_log",
            column: "tenant_id");

        migrationBuilder.Sql("ALTER TABLE public.audit_log ENABLE ROW LEVEL SECURITY;");
        migrationBuilder.Sql("ALTER TABLE public.audit_log FORCE ROW LEVEL SECURITY;");
        migrationBuilder.Sql(
            """
            CREATE POLICY tenant_isolation ON public.audit_log
                USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
                WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON public.audit_log;");
        migrationBuilder.Sql("ALTER TABLE public.audit_log NO FORCE ROW LEVEL SECURITY;");
        migrationBuilder.Sql("ALTER TABLE public.audit_log DISABLE ROW LEVEL SECURITY;");

        migrationBuilder.DropTable(
            name: "audit_log",
            schema: "public");
    }
}
