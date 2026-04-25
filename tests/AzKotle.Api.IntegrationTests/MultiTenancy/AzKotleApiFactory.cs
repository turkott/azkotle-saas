using AzKotle.Application.Abstractions;
using AzKotle.Domain.Common;
using AzKotle.Domain.Entities.Tenants;
using AzKotle.Domain.Entities.Users;
using AzKotle.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace AzKotle.Api.IntegrationTests.MultiTenancy;

public sealed class AzKotleApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    internal const string TestJwtSecret =
        "test-secret-DO-NOT-USE-IN-PRODUCTION-at-least-32-chars-long-filler-xyz";

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

    public TenantId TenantAId { get; private set; }

    public TenantId TenantBId { get; private set; }

    public UserId UserAId { get; private set; }

    public UserId UserBId { get; private set; }

    public string TenantASlug => "acme";

    public string TenantBSlug => "globex";

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

        await using (var raw = new NpgsqlConnection(_adminConnectionString))
        {
            await raw.OpenAsync();
            await Execute(raw, $"CREATE ROLE {AppUserName} LOGIN PASSWORD '{AppUserPassword}' NOSUPERUSER NOBYPASSRLS;");
            await Execute(raw, $"GRANT CONNECT ON DATABASE {adminBuilder.Database} TO {AppUserName};");
            await Execute(raw, $"GRANT USAGE ON SCHEMA public TO {AppUserName};");
            await Execute(raw, $"GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {AppUserName};");
            await Execute(raw, $"ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO {AppUserName};");
        }

        await using (var adminDb = CreateAdminDbContext())
        {
            var tenantA = Tenant.Create(TenantASlug, "ACME s.r.o.", "12345678");
            var tenantB = Tenant.Create(TenantBSlug, "Globex s.r.o.", "87654321");
            adminDb.Tenants.AddRange(tenantA, tenantB);
            await adminDb.SaveChangesAsync();

            var userA = User.Invite(tenantA.Id, "a@example.com", "User A", UserRole.Owner);
            var userB = User.Invite(tenantB.Id, "b@example.com", "User B", UserRole.Owner);
            adminDb.Users.AddRange(userA, userB);
            await adminDb.SaveChangesAsync();

            TenantAId = tenantA.Id;
            TenantBId = tenantB.Id;
            UserAId = userA.Id;
            UserBId = userB.Id;
        }

        // Force WebHost build so subsequent calls use the configured services.
        _ = Services;
    }

    public new Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    public string IssueJwt(TenantId tenantId, UserId userId, string email, UserRole role)
    {
        using var scope = Services.CreateScope();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        return jwt.IssueAccessToken(userId, tenantId, email, role).Token;
    }

    public AzKotleDbContext CreateAdminDbContext()
    {
        var options = new DbContextOptionsBuilder<AzKotleDbContext>()
            .UseNpgsql(_adminConnectionString)
            .Options;
        return new AzKotleDbContext(options);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:AzKotleDb", _appConnectionString);
        builder.UseSetting("Jwt:Secret", TestJwtSecret);
        builder.UseSetting("Jwt:Issuer", "azkotle-test");
        builder.UseSetting("Jwt:Audience", "azkotle-api-test");
    }

    private static async Task Execute(NpgsqlConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}
