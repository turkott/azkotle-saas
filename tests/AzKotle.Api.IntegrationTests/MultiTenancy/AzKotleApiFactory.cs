using AzKotle.Application.Abstractions;
using AzKotle.Domain.Common;
using AzKotle.Domain.Entities.Tenants;
using AzKotle.Domain.Entities.Users;
using AzKotle.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace AzKotle.Api.IntegrationTests.MultiTenancy;

public sealed class AzKotleApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    internal const string TestJwtSecret =
        "test-secret-DO-NOT-USE-IN-PRODUCTION-at-least-32-chars-long-filler-xyz";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("azkotle")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public TenantId TenantAId { get; private set; }

    public TenantId TenantBId { get; private set; }

    public UserId UserAId { get; private set; }

    public UserId UserBId { get; private set; }

    public string TenantASlug => "acme";

    public string TenantBSlug => "globex";

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AzKotleDbContext>();
        await db.Database.MigrateAsync();

        var tenantA = Tenant.Create(TenantASlug, "ACME s.r.o.", "12345678");
        var tenantB = Tenant.Create(TenantBSlug, "Globex s.r.o.", "87654321");
        db.Tenants.AddRange(tenantA, tenantB);
        await db.SaveChangesAsync();

        var userA = User.Invite(tenantA.Id, "a@example.com", "User A", UserRole.Owner);
        var userB = User.Invite(tenantB.Id, "b@example.com", "User B", UserRole.Owner);
        db.Users.AddRange(userA, userB);
        await db.SaveChangesAsync();

        TenantAId = tenantA.Id;
        TenantBId = tenantB.Id;
        UserAId = userA.Id;
        UserBId = userB.Id;
    }

    public new Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    public string IssueJwt(TenantId tenantId, UserId userId, string email, UserRole role)
    {
        using var scope = Services.CreateScope();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        return jwt.IssueAccessToken(userId, tenantId, email, role).Token;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:AzKotleDb", _postgres.GetConnectionString());
        builder.UseSetting("Jwt:Secret", TestJwtSecret);
        builder.UseSetting("Jwt:Issuer", "azkotle-test");
        builder.UseSetting("Jwt:Audience", "azkotle-api-test");
    }
}
