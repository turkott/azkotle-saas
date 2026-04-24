using AzKotle.Application.Abstractions;
using AzKotle.Domain.Common;
using Microsoft.AspNetCore.Http;

namespace AzKotle.Api.MultiTenancy;

internal sealed class HttpTenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpTenantContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public TenantId? Current
    {
        get
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext is null)
            {
                return null;
            }

            if (httpContext.Items.TryGetValue(TenantContextKeys.TenantIdItemKey, out var value) && value is TenantId tenantId)
            {
                return tenantId;
            }

            return null;
        }
    }
}
