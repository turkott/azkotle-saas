using AzKotle.Api.MultiTenancy;
using AzKotle.Infrastructure.PublicAccess;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AzKotle.Api.Endpoints;

public static class PublicInspectionEndpoints
{
    private const int AccessHashMinLength = 16;
    private const int AccessHashMaxLength = 64;

    public const string RateLimitPolicy = "PublicAccessPolicy";

    public static IEndpointRouteBuilder MapPublicInspectionEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/public/inspections")
            // AllowAnonymousTenantAttribute disables the tenant_required gate in
            // TenantResolutionMiddleware — public visitors don't carry a JWT and
            // app subdomain ('app.az-kotle.cz') is reserved/non-tenant. The
            // service sets tenant context manually after resolving the hash.
            .WithMetadata(new AllowAnonymousTenantAttribute())
            .AllowAnonymous()
            // F17: 30 req/min/IP. Limiter krátkospojí pipeline před endpoint
            // handlerem — odmítnutý request NIKDY nedosáhne PublicInspectionService,
            // takže žádné DB connection ani audit-log row pro brute-force scan.
            .RequireRateLimiting(RateLimitPolicy);

        group.MapGet("/{accessHash}", GetSummaryAsync).WithName("PublicInspectionSummary");
        group.MapGet("/{accessHash}/pdf", DownloadPdfAsync).WithName("PublicInspectionPdf");

        return routes;
    }

    private static async Task<IResult> GetSummaryAsync(
        string accessHash,
        PublicInspectionService service,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (!IsValidHash(accessHash))
        {
            return Results.NotFound();
        }

        var (ip, ua) = GetClient(httpContext);
        var result = await service.GetSummaryAsync(accessHash, ip, ua, ct);
        return result switch
        {
            PublicInspectionLookupResult.Success ok => Results.Ok(ok.Response),
            PublicInspectionLookupResult.NotFound => Results.NotFound(),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    private static async Task<IResult> DownloadPdfAsync(
        string accessHash,
        PublicInspectionService service,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (!IsValidHash(accessHash))
        {
            return Results.NotFound();
        }

        var (ip, ua) = GetClient(httpContext);
        var result = await service.IssuePdfUrlAsync(accessHash, ip, ua, ct);
        return result switch
        {
            PublicInspectionPdfResult.Success ok =>
                Results.Redirect(ok.Url, permanent: false, preserveMethod: false),
            PublicInspectionPdfResult.NotFound => Results.NotFound(),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    private static bool IsValidHash(string? accessHash) =>
        !string.IsNullOrWhiteSpace(accessHash)
        && accessHash.Length is >= AccessHashMinLength and <= AccessHashMaxLength
        && accessHash.All(IsAccessHashChar);

    private static bool IsAccessHashChar(char c) =>
        c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-' or '_';

    private static (string? Ip, string? UserAgent) GetClient(HttpContext httpContext)
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString();
        var ua = httpContext.Request.Headers.UserAgent.ToString();
        return (ip, string.IsNullOrWhiteSpace(ua) ? null : ua);
    }
}
