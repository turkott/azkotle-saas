using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AzKotle.Infrastructure.Persistence.Migrations;

/// <summary>
/// F9 (Audit) — DB-level append-only enforcement na audit_log.
///
/// Defense-in-depth k REVOKE UPDATE/DELETE z role azkotle_app (provedeno
/// ve Sprintu 0 přes deploy/postgres/init/01-create-app-role.sh). Pokud by
/// někdo v budoucnu omylem GRANTl práva zpět (refaktoring init scriptu,
/// manuální zásah), trigger DML stále zamítne — i pro superusera.
///
/// Trigger se aplikuje pro VŠECHNY role včetně superusera (postgres triggery
/// neumí role-based bypass; jediná možnost obejít je DROP TRIGGER, což je
/// auditovatelná akce).
/// </summary>
public partial class AddAuditLogImmutableTrigger : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE OR REPLACE FUNCTION public.block_audit_log_modification()
            RETURNS trigger AS $$
            BEGIN
                RAISE EXCEPTION 'audit_log is append-only — UPDATE and DELETE are not permitted (NV 191/2022 evidenční povinnost)'
                    USING ERRCODE = 'insufficient_privilege';
            END;
            $$ LANGUAGE plpgsql;
            """);

        migrationBuilder.Sql(
            """
            CREATE TRIGGER trg_audit_log_immutable
                BEFORE UPDATE OR DELETE ON public.audit_log
                FOR EACH ROW EXECUTE FUNCTION public.block_audit_log_modification();
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_audit_log_immutable ON public.audit_log;");
        migrationBuilder.Sql("DROP FUNCTION IF EXISTS public.block_audit_log_modification();");
    }
}
