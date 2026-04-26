using AzKotle.Api.MultiTenancy;
using AzKotle.Application.Abstractions;
using AzKotle.Application.Auth;
using AzKotle.Domain.Common;
using AzKotle.Domain.Entities.Auth;
using AzKotle.Domain.Entities.Tenants;
using AzKotle.Domain.Entities.Users;
using AzKotle.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AzKotle.Api.Endpoints;

public static class AuthEndpoints
{
    private const string UniqueViolationSqlState = "23505";

    /// <summary>
    /// HttpOnly Secure SameSite=Strict cookie obsahující refresh token.
    /// Path=/api/v1/auth omezuje kdy ji prohlížeč pošle (refresh + logout).
    /// </summary>
    internal const string RefreshCookieName = "azkotle_refresh";
    internal const string RefreshCookiePath = "/api/v1/auth";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/auth")
            .RequireRateLimiting("auth");

        group.MapPost("/register", RegisterAsync)
            .WithName("AuthRegister")
            .WithMetadata(new AllowAnonymousTenantAttribute())
            .AllowAnonymous();

        group.MapPost("/login", LoginAsync)
            .WithName("AuthLogin")
            .WithMetadata(new AllowAnonymousTenantAttribute())
            .AllowAnonymous();

        group.MapPost("/refresh", RefreshAsync)
            .WithName("AuthRefresh")
            .WithMetadata(new AllowAnonymousTenantAttribute())
            .AllowAnonymous();

        // Logout NEMÁ RequireAuthorization — refresh cookie sama prokazuje identitu
        // (HttpOnly + SameSite=Strict eliminuje cross-site abuse). Bez toho by
        // uživatel s expirovaným access tokenem nemohl odhlásit a serverová cookie
        // by žila až do své Max-Age (30 dní), zbytečně si držela DB záznam.
        group.MapPost("/logout", LogoutAsync)
            .WithName("AuthLogout")
            .WithMetadata(new AllowAnonymousTenantAttribute())
            .AllowAnonymous();

        return routes;
    }

    private static async Task<IResult> RegisterAsync(
        [FromBody] RegisterRequest request,
        IValidator<RegisterRequest> validator,
        AzKotleDbContext db,
        IPasswordHasher hasher,
        IJwtTokenService jwt,
        IOptions<JwtOptions> jwtOptions,
        HttpContext httpContext,
        TimeProvider time,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.ToDictionary());
        }

        var slugTaken = await db.Tenants
            .AsNoTracking()
            .AnyAsync(t => t.Slug == request.TenantSlug, ct);
        if (slugTaken)
        {
            return Results.Conflict(new { error = "slug_taken" });
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var tenant = Tenant.Create(request.TenantSlug, request.CompanyName, request.Ico, time);
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(ct);

        await db.Database.ExecuteSqlRawAsync(
            "SELECT set_config('app.current_tenant_id', {0}, false)",
            new object[] { tenant.Id.Value.ToString() },
            ct);

        var passwordHash = hasher.Hash(request.Password);
        var user = User.RegisterOwner(tenant.Id, request.Email, request.FullName, passwordHash, time);
        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return Results.Conflict(new { error = "email_taken" });
        }

        var issued = await IssueTokensAndSaveAsync(db, jwt, tenant.Id, user, time, ct);
        await tx.CommitAsync(ct);

        // Cookie nastavujeme AŽ po commitu — kdyby commit failnul, klient
        // nedostane cookie odkazující na neexistující DB záznam.
        SetRefreshCookie(httpContext, issued.PlainRefreshToken, jwtOptions.Value);
        return Results.Ok(issued.Public);
    }

    private static async Task<IResult> LoginAsync(
        [FromBody] LoginRequest request,
        IValidator<LoginRequest> validator,
        AzKotleDbContext db,
        ITenantContext tenantContext,
        IPasswordHasher hasher,
        IJwtTokenService jwt,
        IOptions<JwtOptions> jwtOptions,
        HttpContext httpContext,
        TimeProvider time,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.ToDictionary());
        }

        var tenantId = await ResolveTenantAsync(db, tenantContext, request.TenantSlug, ct);
        if (tenantId is null)
        {
            return InvalidCredentials();
        }

        await db.Database.ExecuteSqlRawAsync(
            "SELECT set_config('app.current_tenant_id', {0}, false)",
            new object[] { tenantId.Value.Value.ToString() },
            ct);

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail, ct);

        if (user is null
            || !user.IsActive
            || string.IsNullOrEmpty(user.PasswordHash)
            || user.TenantId != tenantId.Value
            || !hasher.Verify(request.Password, user.PasswordHash))
        {
            return InvalidCredentials();
        }

        user.RecordLogin(time);
        var issued = await IssueTokensAndSaveAsync(db, jwt, tenantId.Value, user, time, ct);
        SetRefreshCookie(httpContext, issued.PlainRefreshToken, jwtOptions.Value);
        return Results.Ok(issued.Public);
    }

    private static async Task<IResult> RefreshAsync(
        [FromBody] RefreshRequest? request,
        IValidator<RefreshRequest> validator,
        AzKotleDbContext db,
        ITenantContext tenantContext,
        IJwtTokenService jwt,
        IOptions<JwtOptions> jwtOptions,
        HttpContext httpContext,
        ILogger<RefreshTokenScope> logger,
        TimeProvider time,
        CancellationToken ct)
    {
        if (!httpContext.Request.Cookies.TryGetValue(RefreshCookieName, out var refreshToken)
            || string.IsNullOrWhiteSpace(refreshToken))
        {
            logger.LogWarning("Refresh attempted without {CookieName} cookie (Origin={Origin})",
                RefreshCookieName, httpContext.Request.Headers.Origin.ToString());
            return InvalidRefresh();
        }

        var validation = await validator.ValidateAsync(request ?? new RefreshRequest(), ct);
        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.ToDictionary());
        }

        var tenantId = await ResolveTenantAsync(db, tenantContext, request?.TenantSlug, ct);
        if (tenantId is null)
        {
            logger.LogWarning("Refresh failed: tenant unresolved (no JWT, no subdomain, no body slug)");
            return InvalidRefresh();
        }

        await db.Database.ExecuteSqlRawAsync(
            "SELECT set_config('app.current_tenant_id', {0}, false)",
            new object[] { tenantId.Value.Value.ToString() },
            ct);

        var hash = jwt.HashRefreshToken(refreshToken);
        var existing = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (existing is null)
        {
            logger.LogWarning("Refresh failed: token hash not found (tenantId={TenantId})", tenantId.Value.Value);
            return InvalidRefresh();
        }

        var now = time.GetUtcNow().UtcDateTime;
        if (!existing.IsActive(now))
        {
            if (existing.ReplacedById is not null)
            {
                logger.LogWarning(
                    "Refresh token reuse detected — revoking chain (userId={UserId}, tenantId={TenantId})",
                    existing.UserId.Value, existing.TenantId.Value);
                await RevokeChainAsync(db, existing, now, ct);
                await db.SaveChangesAsync(ct);
            }
            else
            {
                logger.LogWarning("Refresh failed: token revoked or expired (userId={UserId})", existing.UserId.Value);
            }
            return InvalidRefresh();
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == existing.UserId, ct);
        if (user is null || !user.IsActive)
        {
            logger.LogWarning("Refresh failed: user inactive or missing (userId={UserId})", existing.UserId.Value);
            return InvalidRefresh();
        }

        var (newToken, newHash, newExpiresAt) = jwt.GenerateRefreshToken();
        var replacement = RefreshToken.Issue(existing.TenantId, existing.UserId, newHash, newExpiresAt, time);
        db.RefreshTokens.Add(replacement);
        existing.RevokeAndReplace(replacement.Id, time);
        await db.SaveChangesAsync(ct);

        var access = jwt.IssueAccessToken(user.Id, existing.TenantId, user.Email, user.Role);
        SetRefreshCookie(httpContext, newToken, jwtOptions.Value);
        return Results.Ok(new AuthResponse(
            access.Token,
            access.ExpiresInSeconds,
            user.Id.Value,
            existing.TenantId.Value,
            user.Email,
            user.Role.ToString()));
    }

    private static async Task<IResult> LogoutAsync(
        AzKotleDbContext db,
        IJwtTokenService jwt,
        HttpContext httpContext,
        TimeProvider time,
        CancellationToken ct)
    {
        if (httpContext.Request.Cookies.TryGetValue(RefreshCookieName, out var refreshToken)
            && !string.IsNullOrWhiteSpace(refreshToken))
        {
            var hash = jwt.HashRefreshToken(refreshToken);
            var existing = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
            if (existing is not null && existing.RevokedAt is null)
            {
                existing.Revoke(time);
                await db.SaveChangesAsync(ct);
            }
        }

        // Vždy mažeme cookie u klienta — i když token v DB nenajdem nebo cookie chyběla.
        ClearRefreshCookie(httpContext);
        return Results.NoContent();
    }

    private static async Task<IssuedTokens> IssueTokensAndSaveAsync(
        AzKotleDbContext db,
        IJwtTokenService jwt,
        TenantId tenantId,
        User user,
        TimeProvider time,
        CancellationToken ct)
    {
        var access = jwt.IssueAccessToken(user.Id, tenantId, user.Email, user.Role);
        var (refreshToken, refreshHash, refreshExpiresAt) = jwt.GenerateRefreshToken();
        var rt = RefreshToken.Issue(tenantId, user.Id, refreshHash, refreshExpiresAt, time);
        db.RefreshTokens.Add(rt);
        await db.SaveChangesAsync(ct);

        var publicResponse = new AuthResponse(
            access.Token,
            access.ExpiresInSeconds,
            user.Id.Value,
            tenantId.Value,
            user.Email,
            user.Role.ToString());

        return new IssuedTokens(publicResponse, refreshToken);
    }

    private static void SetRefreshCookie(HttpContext ctx, string token, JwtOptions options)
    {
        ctx.Response.Cookies.Append(RefreshCookieName, token, BuildCookieOptions(TimeSpan.FromDays(options.RefreshTokenDays)));
    }

    private static void ClearRefreshCookie(HttpContext ctx)
    {
        // Prázdná hodnota + MaxAge=0 — prohlížeč cookie hned odstraní.
        // Atributy musí přesně sedět s puvodním Set-Cookie (Path, SameSite, Secure, HttpOnly), jinak browser nezruší správnou cookie.
        ctx.Response.Cookies.Append(RefreshCookieName, string.Empty, BuildCookieOptions(TimeSpan.Zero));
    }

    private static CookieOptions BuildCookieOptions(TimeSpan maxAge) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = RefreshCookiePath,
        MaxAge = maxAge,
        // Domain unset — defaultuje na request host (api.az-kotle.cz). Záměrně NE
        // .az-kotle.cz, abychom nesdíleli cookie s app/admin subdoménami.
    };

    private static async Task RevokeChainAsync(
        AzKotleDbContext db,
        RefreshToken reusedToken,
        DateTime now,
        CancellationToken ct)
    {
        var current = reusedToken;
        while (current.ReplacedById is not null)
        {
            var next = await db.RefreshTokens.FirstOrDefaultAsync(t => t.Id == current.ReplacedById.Value, ct);
            if (next is null)
            {
                break;
            }
            if (next.RevokedAt is null)
            {
                next.Revoke();
            }
            current = next;
        }
    }

    private static async Task<TenantId?> ResolveTenantAsync(
        AzKotleDbContext db,
        ITenantContext tenantContext,
        string? slugFallback,
        CancellationToken ct)
    {
        if (tenantContext.Current is { } current)
        {
            return current;
        }

        if (string.IsNullOrWhiteSpace(slugFallback))
        {
            return null;
        }

        var guid = await db.Tenants
            .AsNoTracking()
            .Where(t => t.Slug == slugFallback)
            .Select(t => (Guid?)t.Id.Value)
            .FirstOrDefaultAsync(ct);

        return guid.HasValue ? new TenantId(guid.Value) : null;
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg && pg.SqlState == UniqueViolationSqlState;

    private static IResult InvalidCredentials() =>
        Results.Json(new { error = "invalid_credentials" }, statusCode: StatusCodes.Status401Unauthorized);

    private static IResult InvalidRefresh() =>
        Results.Json(new { error = "invalid_refresh_token" }, statusCode: StatusCodes.Status401Unauthorized);

    /// <summary>Marker typ pro <see cref="ILogger{T}"/> kategorii v RefreshAsync.</summary>
    internal sealed class RefreshTokenScope { }

    /// <summary>Interní výsledek <see cref="IssueTokensAndSaveAsync"/> — Public jde do response body, PlainRefreshToken jde do HttpOnly cookie.</summary>
    private sealed record IssuedTokens(AuthResponse Public, string PlainRefreshToken);
}
