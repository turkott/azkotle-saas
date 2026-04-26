using AzKotle.Application.Abstractions;
using AzKotle.Domain.Common;
using AzKotle.Domain.Entities.Tenants;
using AzKotle.Domain.Entities.Users;
using AzKotle.Infrastructure.Persistence;
using AzKotle.Infrastructure.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Testcontainers.Minio;
using Testcontainers.PostgreSql;

namespace AzKotle.Api.IntegrationTests.MultiTenancy;

public class AzKotleApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
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

    private readonly MinioContainer _minio = new MinioBuilder()
        .WithImage("minio/minio:RELEASE.2024-12-13T22-19-12Z")
        .WithUsername("minioadmin")
        .WithPassword("minioadmin")
        .Build();

    private string _adminConnectionString = string.Empty;
    private string _appConnectionString = string.Empty;
    private S3FileStorage? _testStorage;
    private StorageOptions _testStorageOptions = new();

    public string MinioBucket => "azkotle-test";

    public IFileStorage TestStorage => _testStorage
        ?? throw new InvalidOperationException("Storage není inicializované — InitializeAsync ještě neproběhlo.");

    public TenantId TenantAId { get; private set; }

    public TenantId TenantBId { get; private set; }

    public UserId UserAId { get; private set; }

    public UserId UserBId { get; private set; }

    public string TenantASlug => "acme";

    public string TenantBSlug => "globex";

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _minio.StartAsync());
        _adminConnectionString = _postgres.GetConnectionString();

        _testStorageOptions = new StorageOptions
        {
            Bucket = MinioBucket,
            ServiceUrl = $"http://{_minio.Hostname}:{_minio.GetMappedPublicPort(9000)}",
            Region = "us-east-1",
            AccessKey = "minioadmin",
            SecretKey = "minioadmin",
            ForcePathStyle = true,
        };
        _testStorage = new S3FileStorage(Options.Create(_testStorageOptions), NullLogger<S3FileStorage>.Instance);
        await _testStorage.EnsureBucketExistsAsync();

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

    public new async Task DisposeAsync()
    {
        _testStorage?.Dispose();
        await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _minio.DisposeAsync().AsTask());
    }

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
        builder.UseSetting("Storage:Bucket", _testStorageOptions.Bucket);
        builder.UseSetting("Storage:ServiceUrl", _testStorageOptions.ServiceUrl);
        builder.UseSetting("Storage:Region", _testStorageOptions.Region);
        builder.UseSetting("Storage:AccessKey", _testStorageOptions.AccessKey);
        builder.UseSetting("Storage:SecretKey", _testStorageOptions.SecretKey);
        builder.UseSetting("Storage:ForcePathStyle", "true");
        // Default factory: rate limit prakticky vypnutý, aby existující testy
        // nehit 429 limit při více /auth volání v jedné test class.
        // Pro testování samotného limiteru viz AuthRateLimitFactory.
        builder.UseSetting("RateLimit:Auth:PermitLimit", "10000");
    }

    private static async Task Execute(NpgsqlConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}
