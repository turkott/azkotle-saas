using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AzKotle.Infrastructure.Persistence.Migrations;

/// <summary>
/// F14 — public viewer link for customers. Adds <c>access_hash</c> as the
/// unguessable token in the URL <c>/i/{accessHash}</c> plus a SECURITY DEFINER
/// helper function the API uses to resolve the token to (inspection_id, tenant_id)
/// without first having a tenant context (the public visitor isn't logged in).
/// The function bypasses RLS by inheriting the migration owner's privileges
/// (superuser / BYPASSRLS); the security boundary is the 192-bit random hash
/// in the URL, not the role grants.
/// </summary>
public partial class AddInspectionPublicAccess : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Add as nullable first so the backfill can write per-row unique values
        // without colliding on the unique index that comes later.
        migrationBuilder.AddColumn<string>(
            name: "access_hash",
            schema: "public",
            table: "inspections",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true);

        // Backfill: 32 hex chars from gen_random_uuid() (no extension required
        // since Postgres 13). Different format than runtime-generated base64url
        // tokens, but same length and same uniqueness guarantees — the column is
        // opaque to consumers.
        migrationBuilder.Sql(
            """
            UPDATE public.inspections
            SET access_hash = REPLACE(gen_random_uuid()::text, '-', '')
            WHERE access_hash IS NULL;
            """);

        migrationBuilder.AlterColumn<string>(
            name: "access_hash",
            schema: "public",
            table: "inspections",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(32)",
            oldMaxLength: 32,
            oldNullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_inspections_access_hash",
            schema: "public",
            table: "inspections",
            column: "access_hash",
            unique: true);

        // SECURITY DEFINER lookup. Owner = migration runner (superuser → BYPASSRLS),
        // so the function reads inspections regardless of the caller's
        // app.current_tenant_id setting. Returns ONLY rows whose status is Signed
        // — drafts must never be exposed publicly.
        migrationBuilder.Sql(
            """
            CREATE OR REPLACE FUNCTION public.find_public_inspection(p_access_hash text)
            RETURNS TABLE(
                inspection_id uuid,
                tenant_id uuid,
                performed_at timestamptz,
                inspection_type text,
                pdf_b2_key text,
                tenant_company_name text,
                tenant_logo_storage_key text,
                boiler_manufacturer text,
                boiler_model text
            )
            LANGUAGE sql
            SECURITY DEFINER
            STABLE
            SET search_path = public, pg_temp
            AS $$
                SELECT
                    i.id,
                    i.tenant_id,
                    i.performed_at,
                    i.inspection_type,
                    i.pdf_b2_key,
                    t.company_name,
                    t.logo_storage_key,
                    b.manufacturer,
                    b.model
                FROM public.inspections i
                JOIN public.tenants t ON t.id = i.tenant_id
                JOIN public.boilers b ON b.id = i.boiler_id
                WHERE i.access_hash = p_access_hash
                  AND i.status = 'Signed';
            $$;
            """);

        migrationBuilder.Sql(
            "REVOKE ALL ON FUNCTION public.find_public_inspection(text) FROM PUBLIC;");

        // Grant EXECUTE only if the runtime role exists; in dev the API connects
        // as the postgres-owner user (and would inherit EXECUTE via PUBLIC anyway
        // before the REVOKE — keep the explicit grant for prod parity).
        migrationBuilder.Sql(
            """
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'azkotle_app') THEN
                    EXECUTE 'GRANT EXECUTE ON FUNCTION public.find_public_inspection(text) TO azkotle_app';
                END IF;
            END $$;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP FUNCTION IF EXISTS public.find_public_inspection(text);");

        migrationBuilder.DropIndex(
            name: "IX_inspections_access_hash",
            schema: "public",
            table: "inspections");

        migrationBuilder.DropColumn(
            name: "access_hash",
            schema: "public",
            table: "inspections");
    }
}
