using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AzKotle.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddAuthTables : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "password_hash",
            schema: "public",
            table: "users",
            type: "character varying(256)",
            maxLength: 256,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "refresh_tokens",
            schema: "public",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                expires_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                revoked_at = table.Column<DateTime>(type: "timestamptz", nullable: true),
                replaced_by_id = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_refresh_tokens", x => x.id);
                table.ForeignKey(
                    name: "FK_refresh_tokens_users_user_id",
                    column: x => x.user_id,
                    principalSchema: "public",
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_refresh_tokens_tenant_id",
            schema: "public",
            table: "refresh_tokens",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_refresh_tokens_token_hash",
            schema: "public",
            table: "refresh_tokens",
            column: "token_hash",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_refresh_tokens_user_id",
            schema: "public",
            table: "refresh_tokens",
            column: "user_id");

        migrationBuilder.Sql("ALTER TABLE public.refresh_tokens ENABLE ROW LEVEL SECURITY;");
        migrationBuilder.Sql("ALTER TABLE public.refresh_tokens FORCE ROW LEVEL SECURITY;");
        migrationBuilder.Sql(
            """
            CREATE POLICY tenant_isolation ON public.refresh_tokens
                USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
                WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON public.refresh_tokens;");
        migrationBuilder.Sql("ALTER TABLE public.refresh_tokens NO FORCE ROW LEVEL SECURITY;");
        migrationBuilder.Sql("ALTER TABLE public.refresh_tokens DISABLE ROW LEVEL SECURITY;");

        migrationBuilder.DropTable(
            name: "refresh_tokens",
            schema: "public");

        migrationBuilder.DropColumn(
            name: "password_hash",
            schema: "public",
            table: "users");
    }
}
