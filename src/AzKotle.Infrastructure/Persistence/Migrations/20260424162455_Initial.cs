using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AzKotle.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class Initial : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "public");

        migrationBuilder.CreateTable(
            name: "tenants",
            schema: "public",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                company_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ico = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                dic = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                plan = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                seats_limit = table.Column<int>(type: "integer", nullable: true),
                trial_ends_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                updated_at = table.Column<DateTime>(type: "timestamptz", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_tenants", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "users",
            schema: "public",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                full_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                technician_license_no = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                is_active = table.Column<bool>(type: "boolean", nullable: false),
                last_login_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                created_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_users", x => x.id);
                table.ForeignKey(
                    name: "FK_users_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalSchema: "public",
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_tenants_slug",
            schema: "public",
            table: "tenants",
            column: "slug",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_users_email",
            schema: "public",
            table: "users",
            column: "email",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_users_tenant_id",
            schema: "public",
            table: "users",
            column: "tenant_id");

        migrationBuilder.Sql("ALTER TABLE public.users ENABLE ROW LEVEL SECURITY;");
        migrationBuilder.Sql("ALTER TABLE public.users FORCE ROW LEVEL SECURITY;");
        migrationBuilder.Sql(
            """
            CREATE POLICY tenant_isolation ON public.users
                USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
                WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON public.users;");
        migrationBuilder.Sql("ALTER TABLE public.users NO FORCE ROW LEVEL SECURITY;");
        migrationBuilder.Sql("ALTER TABLE public.users DISABLE ROW LEVEL SECURITY;");

        migrationBuilder.DropTable(
            name: "users",
            schema: "public");

        migrationBuilder.DropTable(
            name: "tenants",
            schema: "public");
    }
}
