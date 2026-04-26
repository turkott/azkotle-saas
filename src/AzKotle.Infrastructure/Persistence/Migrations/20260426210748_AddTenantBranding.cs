using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AzKotle.Infrastructure.Persistence.Migrations;

/// <summary>
/// B15 — adds tenant branding columns: logo_storage_key (S3 key for the
/// tenant logo, fixed pattern <c>tenants/{tid}/branding/logo</c>) and
/// logo_updated_at (cache-busting timestamp).
/// </summary>
public partial class AddTenantBranding : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "logo_storage_key",
            schema: "public",
            table: "tenants",
            type: "character varying(256)",
            maxLength: 256,
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "logo_updated_at",
            schema: "public",
            table: "tenants",
            type: "timestamptz",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "logo_storage_key",
            schema: "public",
            table: "tenants");

        migrationBuilder.DropColumn(
            name: "logo_updated_at",
            schema: "public",
            table: "tenants");
    }
}
