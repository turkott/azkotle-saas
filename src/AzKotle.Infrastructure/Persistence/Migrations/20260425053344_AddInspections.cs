using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AzKotle.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddInspections : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "inspections",
            schema: "public",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                boiler_id = table.Column<Guid>(type: "uuid", nullable: false),
                technician_id = table.Column<Guid>(type: "uuid", nullable: false),
                inspection_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                performed_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                form_data = table.Column<string>(type: "jsonb", nullable: false),
                findings = table.Column<string>(type: "text", nullable: true),
                recommendations = table.Column<string>(type: "text", nullable: true),
                next_due_at = table.Column<DateOnly>(type: "date", nullable: true),
                pdf_b2_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                pdf_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                signed_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                signature_data = table.Column<byte[]>(type: "bytea", nullable: true),
                created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                updated_at = table.Column<DateTime>(type: "timestamptz", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_inspections", x => x.id);
                table.ForeignKey(
                    name: "FK_inspections_boilers_boiler_id",
                    column: x => x.boiler_id,
                    principalSchema: "public",
                    principalTable: "boilers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_inspections_users_technician_id",
                    column: x => x.technician_id,
                    principalSchema: "public",
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_inspections_boiler_id",
            schema: "public",
            table: "inspections",
            column: "boiler_id");

        migrationBuilder.CreateIndex(
            name: "IX_inspections_technician_id",
            schema: "public",
            table: "inspections",
            column: "technician_id");

        migrationBuilder.CreateIndex(
            name: "IX_inspections_tenant_id",
            schema: "public",
            table: "inspections",
            column: "tenant_id");

        migrationBuilder.Sql("ALTER TABLE public.inspections ENABLE ROW LEVEL SECURITY;");
        migrationBuilder.Sql("ALTER TABLE public.inspections FORCE ROW LEVEL SECURITY;");
        migrationBuilder.Sql(
            """
            CREATE POLICY tenant_isolation ON public.inspections
                USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
                WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON public.inspections;");
        migrationBuilder.Sql("ALTER TABLE public.inspections NO FORCE ROW LEVEL SECURITY;");
        migrationBuilder.Sql("ALTER TABLE public.inspections DISABLE ROW LEVEL SECURITY;");

        migrationBuilder.DropTable(
            name: "inspections",
            schema: "public");
    }
}
