using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AzKotle.Infrastructure.Persistence.Migrations;

/// <summary>
/// F6/F7 — optimistic concurrency on Inspection via Postgres system column
/// <c>xmin</c>. The column is provided by Postgres for every table; this
/// migration is intentionally empty SQL-wise and exists only to bump the
/// EF Core model snapshot so future migrations don't try to "add" xmin
/// (which would fail at runtime).
/// </summary>
public partial class AddInspectionRowVersion : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // No-op: xmin is a Postgres system column on every table.
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // No-op: cannot drop a Postgres system column.
    }
}
