using System.Net.Mime;
using System.Text.Json;
using AzKotle.Domain.Common;
using Microsoft.AspNetCore.Http;

namespace AzKotle.Api.MultiTenancy;

internal sealed class TenantResolutionMiddleware
{
    private static readonly HashSet<string> _reservedSubdomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "app",
        "api",
        "admin",
        "www",
        "localhost",
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<TenantResolutionMiddleware> _logger;

    public TenantResolutionMiddleware(RequestDelegate next, ILogger<TenantResolutionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, TenantLookup tenantLookup)
    {
        var endpoint = context.GetEndpoint();
        var allowAnonymous = endpoint?.Metadata.GetMetadata<AllowAnonymousTenantAttribute>() is not null;

        var tenantId = ResolveFromJwt(context);
        if (tenantId is null)
        {
            var slug = ExtractSubdomain(context.Request.Host.Host);
            if (slug is not null)
            {
                tenantId = await tenantLookup.ResolveBySlugAsync(slug, context.RequestAborted);
            }
        }

        if (tenantId is null && !allowAnonymous)
        {
            await WriteProblemAsync(context, StatusCodes.Status400BadRequest, "tenant_required",
                "Request must target a tenant (JWT claim tenant_id or tenant subdomain required).");
            return;
        }

        if (tenantId is not null)
        {
            context.Items[TenantContextKeys.TenantIdItemKey] = tenantId.Value;
        }

        await _next(context);
    }

    private TenantId? ResolveFromJwt(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var claim = context.User.FindFirst(TenantContextKeys.TenantIdClaimType);
        if (claim is null || string.IsNullOrWhiteSpace(claim.Value))
        {
            return null;
        }

        if (!Guid.TryParse(claim.Value, out var guid))
        {
            _logger.LogWarning("Invalid tenant_id JWT claim value: {Value}", claim.Value);
            return null;
        }

        return new TenantId(guid);
    }

    private static string? ExtractSubdomain(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        var labels = host.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (labels.Length < 2)
        {
            return null;
        }

        var first = labels[0];
        return _reservedSubdomains.Contains(first) ? null : first;
    }

    private static Task WriteProblemAsync(HttpContext context, int statusCode, string error, string detail)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = MediaTypeNames.Application.Json;
        var payload = JsonSerializer.Serialize(new { error, detail });
        return context.Response.WriteAsync(payload, context.RequestAborted);
    }
}
