using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AzKotle.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddPasswordResetTokens : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "password_reset_tokens",
            schema: "public",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                expires_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                used_at = table.Column<DateTime>(type: "timestamptz", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_password_reset_tokens", x => x.id);
                table.ForeignKey(
                    name: "FK_password_reset_tokens_users_user_id",
                    column: x => x.user_id,
                    principalSchema: "public",
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_password_reset_tokens_tenant_id",
            schema: "public",
            table: "password_reset_tokens",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_password_reset_tokens_token_hash",
            schema: "public",
            table: "password_reset_tokens",
            column: "token_hash",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_password_reset_tokens_user_id",
            schema: "public",
            table: "password_reset_tokens",
            column: "user_id");

        migrationBuilder.Sql("ALTER TABLE public.password_reset_tokens ENABLE ROW LEVEL SECURITY;");
        migrationBuilder.Sql("ALTER TABLE public.password_reset_tokens FORCE ROW LEVEL SECURITY;");
        migrationBuilder.Sql(
            """
            CREATE POLICY tenant_isolation ON public.password_reset_tokens
                USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
                WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON public.password_reset_tokens;");
        migrationBuilder.Sql("ALTER TABLE public.password_reset_tokens NO FORCE ROW LEVEL SECURITY;");
        migrationBuilder.Sql("ALTER TABLE public.password_reset_tokens DISABLE ROW LEVEL SECURITY;");

        migrationBuilder.DropTable(
            name: "password_reset_tokens",
            schema: "public");
    }
}
