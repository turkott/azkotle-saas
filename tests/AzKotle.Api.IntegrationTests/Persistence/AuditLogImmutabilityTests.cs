using AzKotle.Domain.Common;
using AzKotle.Domain.Entities.Audit;
using AzKotle.Domain.Entities.Tenants;
using AzKotle.Domain.Entities.Users;
using AzKotle.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace AzKotle.Api.IntegrationTests.Persistence;

/// <summary>
/// F9 — audit_log musí být fyzicky append-only přes DB trigger, NEZÁVISLE
/// na role-based GRANT/REVOKE. Test úmyslně používá superuser roli (postgres),
/// aby ověřil, že trigger blokuje UPDATE i DELETE i pro toho, kdo standardně
/// RLS i column-level granty obchází.
/// </summary>
public sealed class AuditLogImmutabilityTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("azkotle")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _connectionString = _postgres.GetConnectionString();

        var options = new DbContextOptionsBuilder<AzKotleDbContext>()
            .UseNpgsql(_connectionString)
            .Options;
        await using var db = new AzKotleDbContext(options);
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task UPDATE_on_audit_log_throws_even_for_superuser()
    {
        var (auditLogId, tenantId) = await SeedAuditLogAsync();

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        // Bypass RLS jako superuser — bez tenant kontextu by jinak query 0 řádků.
        await using var setTenant = new NpgsqlCommand(
            "SELECT set_config('app.current_tenant_id', @tid, false)", conn);
        setTenant.Parameters.AddWithValue("tid", tenantId.Value.ToString());
        await setTenant.ExecuteNonQueryAsync();

        await using var update = new NpgsqlCommand(
            "UPDATE public.audit_log SET action = 'tampered' WHERE id = @id", conn);
        update.Parameters.AddWithValue("id", auditLogId.Value);

        var act = async () => await update.ExecuteNonQueryAsync();

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.MessageText.Should().Contain("append-only");
    }

    [Fact]
    public async Task DELETE_on_audit_log_throws_even_for_superuser()
    {
        var (auditLogId, tenantId) = await SeedAuditLogAsync();

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var setTenant = new NpgsqlCommand(
            "SELECT set_config('app.current_tenant_id', @tid, false)", conn);
        setTenant.Parameters.AddWithValue("tid", tenantId.Value.ToString());
        await setTenant.ExecuteNonQueryAsync();

        await using var delete = new NpgsqlCommand(
            "DELETE FROM public.audit_log WHERE id = @id", conn);
        delete.Parameters.AddWithValue("id", auditLogId.Value);

        var act = async () => await delete.ExecuteNonQueryAsync();

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.MessageText.Should().Contain("append-only");
    }

    [Fact]
    public async Task INSERT_on_audit_log_succeeds()
    {
        // Sanity check — append-only NEZNAMENÁ no-write. INSERT musí projít.
        var (auditLogId, _) = await SeedAuditLogAsync();
        auditLogId.Value.Should().NotBe(Guid.Empty);
    }

    private async Task<(AuditLogId Id, TenantId TenantId)> SeedAuditLogAsync()
    {
        var options = new DbContextOptionsBuilder<AzKotleDbContext>()
            .UseNpgsql(_connectionString)
            .Options;
        await using var db = new AzKotleDbContext(options);

        var tenant = Tenant.Create($"audit-{Guid.NewGuid():N}".Substring(0, 24), "Audit Test", "12345678");
        var user = User.Invite(tenant.Id, $"audit-{Guid.NewGuid():N}@test.com", "Audit", UserRole.Owner);
        db.Tenants.Add(tenant);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        // set tenant context tak, aby RLS WITH CHECK na audit_log policy povolila INSERT
        await db.Database.ExecuteSqlRawAsync(
            "SELECT set_config('app.current_tenant_id', {0}, false)",
            new object[] { tenant.Id.Value.ToString() });

        var auditLog = AuditLog.Record(
            tenant.Id, user.Id, "test.created", "test", Guid.NewGuid(),
            ipAddress: null, userAgent: null, metadataJson: null);
        db.AuditLog.Add(auditLog);
        await db.SaveChangesAsync();

        return (auditLog.Id, tenant.Id);
    }
}
