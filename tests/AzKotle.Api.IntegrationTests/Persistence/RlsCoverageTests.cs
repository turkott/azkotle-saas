using AzKotle.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace AzKotle.Api.IntegrationTests.Persistence;

/// <summary>
/// Bezpečnostní pojistka: každá tabulka, která má sloupec <c>tenant_id</c>,
/// musí mít zapnuté RLS, FORCE RLS a policy <c>tenant_isolation</c>. Bez toho
/// jakákoli budoucí migrace, která vytvoří novou tenant tabulku a zapomene RLS,
/// otevře cross-tenant leak. Test failne zelený CI s konkrétním názvem tabulky
/// a chybějícím atributem.
/// </summary>
public sealed class RlsCoverageTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("azkotle")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var options = new DbContextOptionsBuilder<AzKotleDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        await using var db = new AzKotleDbContext(options);
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Every_table_with_tenant_id_has_force_rls_and_tenant_isolation_policy()
    {
        await using var conn = new NpgsqlConnection(_postgres.GetConnectionString());
        await conn.OpenAsync();

        var tenantTables = await GetTenantTablesAsync(conn);
        tenantTables.Should().NotBeEmpty(
            "v repu musí existovat alespoň jedna tabulka s tenant_id sloupcem, " +
            "jinak je tento test bezpředmětný (něco se zlomilo v migracích)");

        var failures = new List<string>();
        foreach (var table in tenantTables)
        {
            var state = await GetRlsStateAsync(conn, table);
            if (!state.RlsEnabled)
            {
                failures.Add($"{table}: RLS není zapnuté (ALTER TABLE ... ENABLE ROW LEVEL SECURITY chybí)");
            }
            if (!state.ForceRls)
            {
                failures.Add($"{table}: FORCE RLS není zapnuté — owner role pak RLS obchází (ALTER TABLE ... FORCE ROW LEVEL SECURITY chybí)");
            }
            if (!state.HasTenantIsolationPolicy)
            {
                failures.Add($"{table}: chybí policy 'tenant_isolation' v pg_policies");
            }
        }

        failures.Should().BeEmpty(
            "každá tabulka s tenant_id sloupcem musí mít FORCE RLS + tenant_isolation policy. " +
            "Pokud tento test failne, nová migrace zapomněla nastavit RLS na nově přidané tabulce. " +
            "Viz vzor v migraci 20260424162455_Initial.cs (users) nebo 20260425091041_AddAuditLog.cs.");
    }

    [Fact]
    public async Task Tenants_table_intentionally_has_no_rls_for_pre_auth_lookup()
    {
        // Tenants tabulka NEMÁ RLS úmyslně — slug → id lookup probíhá před tím,
        // než JWT/middleware nastaví tenant context (login, register slug check).
        // Tento test fixuje záměr, aby ho někdo později neopravil "pro konzistenci".
        await using var conn = new NpgsqlConnection(_postgres.GetConnectionString());
        await conn.OpenAsync();

        var state = await GetRlsStateAsync(conn, "tenants");
        state.RlsEnabled.Should().BeFalse(
            "tenants tabulka musí být dostupná bez tenant kontextu (pre-auth slug lookup). " +
            "Pokud chceš tohle změnit, doplň fallback v AuthEndpoints.ResolveTenantAsync.");
    }

    private static async Task<List<string>> GetTenantTablesAsync(NpgsqlConnection conn)
    {
        const string sql = """
            SELECT c.relname
            FROM pg_attribute a
            JOIN pg_class c ON c.oid = a.attrelid
            WHERE a.attname = 'tenant_id'
              AND c.relkind = 'r'
              AND c.relnamespace = 'public'::regnamespace
              AND a.attnum > 0
              AND NOT a.attisdropped
            ORDER BY c.relname
            """;
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        var result = new List<string>();
        while (await reader.ReadAsync())
        {
            result.Add(reader.GetString(0));
        }
        return result;
    }

    private static async Task<RlsState> GetRlsStateAsync(NpgsqlConnection conn, string tableName)
    {
        const string sql = """
            SELECT
                c.relrowsecurity,
                c.relforcerowsecurity,
                EXISTS(
                    SELECT 1 FROM pg_policies
                    WHERE schemaname = 'public'
                      AND tablename = @table
                      AND policyname = 'tenant_isolation'
                )
            FROM pg_class c
            WHERE c.oid = ('public.' || @table)::regclass
            """;
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("table", tableName);
        await using var reader = await cmd.ExecuteReaderAsync();
        var ok = await reader.ReadAsync();
        ok.Should().BeTrue($"tabulka {tableName} musí existovat v pg_class");
        return new RlsState(
            RlsEnabled: reader.GetBoolean(0),
            ForceRls: reader.GetBoolean(1),
            HasTenantIsolationPolicy: reader.GetBoolean(2));
    }

    private sealed record RlsState(bool RlsEnabled, bool ForceRls, bool HasTenantIsolationPolicy);
}
