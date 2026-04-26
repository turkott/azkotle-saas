using System.Security.Claims;
using AzKotle.Application.Abstractions;
using AzKotle.Domain.Common;
using AzKotle.Infrastructure.Tenants;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace AzKotle.Api.Endpoints;

public static class TenantBrandingEndpoints
{
    public static IEndpointRouteBuilder MapTenantBrandingEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/tenant/branding").RequireAuthorization();

        group.MapPost("/logo", UploadLogoAsync)
            .WithName("TenantBrandingUploadLogo")
            .DisableAntiforgery();

        return routes;
    }

    private static async Task<IResult> UploadLogoAsync(
        IFormFile file,
        TenantBrandingService brandingService,
        ITenantContext tenantContext,
        ClaimsPrincipal user,
        HttpContext httpContext,
        CancellationToken ct)
    {
        // JwtBearer default remaps "role" → ClaimTypes.Role, so check both.
        var role = user.FindFirstValue("role") ?? user.FindFirstValue(ClaimTypes.Role);
        if (role != "Owner")
        {
            return Results.Forbid();
        }
        if (!TryGetUserId(user, out var actorId))
        {
            return Results.Unauthorized();
        }
        if (tenantContext.Current is not { } tenantId)
        {
            return Results.Unauthorized();
        }
        if (file is null || file.Length == 0)
        {
            return Results.BadRequest(new { error = "invalid_input", detail = "Soubor chybí." });
        }
        if (file.Length > TenantBrandingService.MaxLogoBytes)
        {
            return Results.BadRequest(new
            {
                error = "invalid_input",
                detail = $"Soubor je příliš velký, maximum je {TenantBrandingService.MaxLogoBytes / 1024} KB.",
            });
        }

        using var ms = new MemoryStream((int)file.Length);
        await file.CopyToAsync(ms, ct);

        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = httpContext.Request.Headers.UserAgent.ToString();
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            userAgent = null;
        }

        var result = await brandingService.UploadLogoAsync(
            tenantId, actorId, ms.ToArray(), file.ContentType, ipAddress, userAgent, ct);

        return result switch
        {
            UploadLogoResult.Success ok => Results.Ok(new TenantBrandingResponse(ok.StorageKey, ok.LogoUpdatedAt)),
            UploadLogoResult.Invalid bad => Results.BadRequest(new { error = "invalid_input", detail = bad.Reason }),
            UploadLogoResult.NotFound => Results.NotFound(),
            _ => Results.StatusCode(500),
        };
    }

    private static bool TryGetUserId(ClaimsPrincipal user, out UserId userId)
    {
        userId = UserId.Empty;
        var sub = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(sub, out var guid))
        {
            userId = new UserId(guid);
            return true;
        }
        return false;
    }
}

public sealed record TenantBrandingResponse(string LogoStorageKey, DateTime LogoUpdatedAt);
