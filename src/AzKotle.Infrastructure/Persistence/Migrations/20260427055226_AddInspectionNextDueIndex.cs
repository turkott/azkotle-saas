using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AzKotle.Infrastructure.Persistence.Migrations;

/// <summary>
/// F21 — composite partial index supporting the dashboard "upcoming expirations"
/// query. Fields: tenant_id (always filtered first by RLS / explicit predicate),
/// status (we want only Signed), next_due_at (range scan).
///
/// Partial WHERE next_due_at IS NOT NULL keeps the index small — drafts and
/// signed-but-no-due-date rows (Service, Emergency types from F13) are excluded.
/// Without this index, a tenant with thousands of inspections forces a sequential
/// scan + filter + sort for every dashboard load.
///
/// Not expressed via fluent HasIndex+HasFilter because EF model snapshot then
/// drifts from the partial-index syntax that Postgres needs; raw SQL is the
/// canonical pattern in this repo (see AddTenantBranding, AddAuditLogImmutableTrigger).
/// </summary>
public partial class AddInspectionNextDueIndex : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE INDEX IF NOT EXISTS ix_inspections_tenant_status_next_due
                ON public.inspections (tenant_id, status, next_due_at)
                WHERE next_due_at IS NOT NULL;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP INDEX IF EXISTS public.ix_inspections_tenant_status_next_due;");
    }
}
