using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AzKotle.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddCustomersLocationsBoilers : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "customers",
            schema: "public",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ico = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                notes = table.Column<string>(type: "text", nullable: true),
                created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                updated_at = table.Column<DateTime>(type: "timestamptz", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_customers", x => x.id);
                table.ForeignKey(
                    name: "FK_customers_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalSchema: "public",
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "locations",
            schema: "public",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                street = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                city = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                zip = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                gps_lat = table.Column<decimal>(type: "numeric(10,7)", nullable: true),
                gps_lon = table.Column<decimal>(type: "numeric(10,7)", nullable: true),
                notes = table.Column<string>(type: "text", nullable: true),
                created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                updated_at = table.Column<DateTime>(type: "timestamptz", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_locations", x => x.id);
                table.ForeignKey(
                    name: "FK_locations_customers_customer_id",
                    column: x => x.customer_id,
                    principalSchema: "public",
                    principalTable: "customers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "boilers",
            schema: "public",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                location_id = table.Column<Guid>(type: "uuid", nullable: false),
                qr_code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                manufacturer = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                serial_no = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                output_kw = table.Column<decimal>(type: "numeric(5,1)", nullable: false),
                fuel_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                installed_at = table.Column<DateOnly>(type: "date", nullable: false),
                last_inspection_at = table.Column<DateOnly>(type: "date", nullable: true),
                next_inspection_due = table.Column<DateOnly>(type: "date", nullable: true),
                notes = table.Column<string>(type: "text", nullable: true),
                created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                updated_at = table.Column<DateTime>(type: "timestamptz", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_boilers", x => x.id);
                table.ForeignKey(
                    name: "FK_boilers_locations_location_id",
                    column: x => x.location_id,
                    principalSchema: "public",
                    principalTable: "locations",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_boilers_location_id",
            schema: "public",
            table: "boilers",
            column: "location_id");

        migrationBuilder.CreateIndex(
            name: "IX_boilers_qr_code",
            schema: "public",
            table: "boilers",
            column: "qr_code",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_boilers_tenant_id",
            schema: "public",
            table: "boilers",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_customers_tenant_id",
            schema: "public",
            table: "customers",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_locations_customer_id",
            schema: "public",
            table: "locations",
            column: "customer_id");

        migrationBuilder.CreateIndex(
            name: "IX_locations_tenant_id",
            schema: "public",
            table: "locations",
            column: "tenant_id");

        foreach (var table in new[] { "customers", "locations", "boilers" })
        {
            migrationBuilder.Sql($"ALTER TABLE public.{table} ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql($"ALTER TABLE public.{table} FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                $"""
                CREATE POLICY tenant_isolation ON public.{table}
                    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
                """);
        }
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var table in new[] { "boilers", "locations", "customers" })
        {
            migrationBuilder.Sql($"DROP POLICY IF EXISTS tenant_isolation ON public.{table};");
            migrationBuilder.Sql($"ALTER TABLE public.{table} NO FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql($"ALTER TABLE public.{table} DISABLE ROW LEVEL SECURITY;");
        }

        migrationBuilder.DropTable(
            name: "boilers",
            schema: "public");

        migrationBuilder.DropTable(
            name: "locations",
            schema: "public");

        migrationBuilder.DropTable(
            name: "customers",
            schema: "public");
    }
}
