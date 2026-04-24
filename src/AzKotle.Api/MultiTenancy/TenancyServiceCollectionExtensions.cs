using AzKotle.Application.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AzKotle.Api.MultiTenancy;

public static class TenancyServiceCollectionExtensions
{
    public static IServiceCollection AddAzKotleHttpTenancy(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddMemoryCache();
        services.AddScoped<TenantLookup>();

        services.Replace(ServiceDescriptor.Singleton<ITenantContext, HttpTenantContext>());

        return services;
    }

    public static IApplicationBuilder UseTenantResolution(this IApplicationBuilder app)
    {
        return app.UseMiddleware<TenantResolutionMiddleware>();
    }
}
