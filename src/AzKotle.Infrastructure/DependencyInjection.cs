using AzKotle.Application.Abstractions;
using AzKotle.Infrastructure.MultiTenancy;
using AzKotle.Infrastructure.Persistence;
using AzKotle.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AzKotle.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAzKotleDb(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddSingleton<AmbientTenantContext>();
        services.AddSingleton<ITenantContext>(sp => sp.GetRequiredService<AmbientTenantContext>());
        services.AddSingleton<TenantContextInterceptor>();

        services.AddDbContext<AzKotleDbContext>((sp, options) =>
        {
            options
                .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(AzKotleDbContext).Assembly.FullName))
                .AddInterceptors(sp.GetRequiredService<TenantContextInterceptor>());
        });

        return services;
    }
}
