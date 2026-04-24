using System.Security.Claims;
using AzKotle.Domain.Common;
using AzKotle.Domain.Entities.Tenants;
using AzKotle.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace AzKotle.Api.IntegrationTests.MultiTenancy;

public sealed class AzKotleApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    internal const string TestTenantClaimHeader = "X-Test-Tenant-Claim";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("azkotle")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public TenantId TenantAId { get; private set; }

    public TenantId TenantBId { get; private set; }

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

        TenantAId = tenantA.Id;
        TenantBId = tenantB.Id;
    }

    public new Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:AzKotleDb", _postgres.GetConnectionString());
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IStartupFilter, TestHeaderAuthStartupFilter>();
        });
    }

    private sealed class TestHeaderAuthStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                app.Use(async (ctx, nextMiddleware) =>
                {
                    if (ctx.Request.Headers.TryGetValue(TestTenantClaimHeader, out var claimValue)
                        && !string.IsNullOrWhiteSpace(claimValue))
                    {
                        var identity = new ClaimsIdentity(
                            new[] { new Claim("tenant_id", claimValue!) },
                            authenticationType: "Test");
                        ctx.User = new ClaimsPrincipal(identity);
                    }

                    await nextMiddleware();
                });

                next(app);
            };
        }
    }
}
