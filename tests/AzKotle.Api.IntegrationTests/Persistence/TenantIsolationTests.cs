using AzKotle.Application.Abstractions;
using AzKotle.Domain.Common;
using AzKotle.Domain.Entities.Tenants;
using AzKotle.Domain.Entities.Users;
using AzKotle.Infrastructure.MultiTenancy;
using AzKotle.Infrastructure.Persistence;
using AzKotle.Infrastructure.Persistence.Interceptors;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace AzKotle.Api.IntegrationTests.Persistence;

public sealed class TenantIsolationTests : IAsyncLifetime
{
    private const string AppUserName = "azkotle_app";
    private const string AppUserPassword = "azkotle_app_pwd";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("azkotle")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private string _adminConnectionString = string.Empty;
    private string _appConnectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _adminConnectionString = _postgres.GetConnectionString();

        var adminBuilder = new NpgsqlConnectionStringBuilder(_adminConnectionString);
        var appBuilder = new NpgsqlConnectionStringBuilder(_adminConnectionString)
        {
            Username = AppUserName,
            Password = AppUserPassword,
        };
        _appConnectionString = appBuilder.ConnectionString;

        await using (var adminDb = CreateAdminDbContext())
        {
            await adminDb.Database.MigrateAsync();
        }

        await using var adminConn = new NpgsqlConnection(_adminConnectionString);
        await adminConn.OpenAsync();
        await ExecuteAsync(adminConn,
            $"CREATE ROLE {AppUserName} LOGIN PASSWORD '{AppUserPassword}' NOSUPERUSER NOBYPASSRLS;");
        await ExecuteAsync(adminConn,
            $"GRANT CONNECT ON DATABASE {adminBuilder.Database} TO {AppUserName};");
        await ExecuteAsync(adminConn,
            $"GRANT USAGE ON SCHEMA public TO {AppUserName};");
        await ExecuteAsync(adminConn,
            $"GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {AppUserName};");
        await ExecuteAsync(adminConn,
            $"ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO {AppUserName};");
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Query_As_Tenant_A_Returns_Only_Tenant_A_Users()
    {
        var (tenantA, tenantB, userA, userB) = await SeedAsync();

        var ambientContext = new AmbientTenantContext();
        using (ambientContext.BeginScope(tenantA.Id))
        {
            await using var db = CreateAppDbContext(ambientContext);
            var users = await db.Users.AsNoTracking().ToListAsync();

            users.Should().ContainSingle();
            users[0].Id.Should().Be(userA.Id);
            users.Should().NotContain(u => u.Id == userB.Id);
        }
    }

    [Fact]
    public async Task Query_As_Tenant_B_Returns_Only_Tenant_B_Users()
    {
        var (tenantA, tenantB, userA, userB) = await SeedAsync();

        var ambientContext = new AmbientTenantContext();
        using (ambientContext.BeginScope(tenantB.Id))
        {
            await using var db = CreateAppDbContext(ambientContext);
            var users = await db.Users.AsNoTracking().ToListAsync();

            users.Should().ContainSingle();
            users[0].Id.Should().Be(userB.Id);
        }
    }

    [Fact]
    public async Task Query_Without_Tenant_Returns_No_Users()
    {
        await SeedAsync();

        var ambientContext = new AmbientTenantContext();
        await using var db = CreateAppDbContext(ambientContext);
        var users = await db.Users.AsNoTracking().ToListAsync();

        users.Should().BeEmpty();
    }

    [Fact]
    public async Task Insert_As_Tenant_A_Rejects_Row_With_Tenant_B_Id()
    {
        var (tenantA, tenantB, _, _) = await SeedAsync();

        var ambientContext = new AmbientTenantContext();
        using (ambientContext.BeginScope(tenantA.Id))
        {
            await using var db = CreateAppDbContext(ambientContext);
            var foreignUser = User.Invite(tenantB.Id, "leak@example.com", "Leak", UserRole.Technician);
            db.Users.Add(foreignUser);

            var act = async () => await db.SaveChangesAsync();
            await act.Should().ThrowAsync<DbUpdateException>();
        }
    }

    private async Task<(Tenant tenantA, Tenant tenantB, User userA, User userB)> SeedAsync()
    {
        var tenantA = Tenant.Create("tenant-a", "Tenant A s.r.o.", "12345678");
        var tenantB = Tenant.Create("tenant-b", "Tenant B s.r.o.", "87654321");
        var userA = User.Invite(tenantA.Id, "a@example.com", "User A", UserRole.Owner);
        var userB = User.Invite(tenantB.Id, "b@example.com", "User B", UserRole.Owner);

        await using var db = CreateAdminDbContext();
        db.Tenants.AddRange(tenantA, tenantB);
        db.Users.AddRange(userA, userB);
        await db.SaveChangesAsync();

        return (tenantA, tenantB, userA, userB);
    }

    private AzKotleDbContext CreateAdminDbContext()
    {
        var options = new DbContextOptionsBuilder<AzKotleDbContext>()
            .UseNpgsql(_adminConnectionString)
            .Options;
        return new AzKotleDbContext(options);
    }

    private AzKotleDbContext CreateAppDbContext(ITenantContext tenantContext)
    {
        var options = new DbContextOptionsBuilder<AzKotleDbContext>()
            .UseNpgsql(_appConnectionString)
            .AddInterceptors(new TenantContextInterceptor(tenantContext))
            .Options;
        return new AzKotleDbContext(options);
    }

    private static async Task ExecuteAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}
