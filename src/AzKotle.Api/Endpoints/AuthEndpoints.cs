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
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AzKotle.Api.Endpoints;

public static class AuthEndpoints
{
    private const string UniqueViolationSqlState = "23505";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/auth");

        group.MapPost("/register", RegisterAsync)
            .WithName("AuthRegister")
            .WithMetadata(new AllowAnonymousTenantAttribute())
            .AllowAnonymous();

        group.MapPost("/login", LoginAsync)
            .WithName("AuthLogin")
            .AllowAnonymous();

        group.MapPost("/refresh", RefreshAsync)
            .WithName("AuthRefresh")
            .AllowAnonymous();

        group.MapPost("/logout", LogoutAsync)
            .WithName("AuthLogout")
            .RequireAuthorization();

        return routes;
    }

    private static async Task<IResult> RegisterAsync(
        [FromBody] RegisterRequest request,
        IValidator<RegisterRequest> validator,
        AzKotleDbContext db,
        IPasswordHasher hasher,
        IJwtTokenService jwt,
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

        var response = await IssueTokensAndSaveAsync(db, jwt, tenant.Id, user, time, ct);
        await tx.CommitAsync(ct);
        return Results.Ok(response);
    }

    private static async Task<IResult> LoginAsync(
        [FromBody] LoginRequest request,
        IValidator<LoginRequest> validator,
        AzKotleDbContext db,
        ITenantContext tenantContext,
        IPasswordHasher hasher,
        IJwtTokenService jwt,
        TimeProvider time,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.ToDictionary());
        }

        var tenantId = tenantContext.Current;
        if (tenantId is null)
        {
            return InvalidCredentials();
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail, ct);

        if (user is null
            || !user.IsActive
            || string.IsNullOrEmpty(user.PasswordHash)
            || !hasher.Verify(request.Password, user.PasswordHash))
        {
            return InvalidCredentials();
        }

        user.RecordLogin(time);
        var response = await IssueTokensAndSaveAsync(db, jwt, tenantId.Value, user, time, ct);
        return Results.Ok(response);
    }

    private static async Task<IResult> RefreshAsync(
        [FromBody] RefreshRequest request,
        IValidator<RefreshRequest> validator,
        AzKotleDbContext db,
        ITenantContext tenantContext,
        IJwtTokenService jwt,
        TimeProvider time,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.ToDictionary());
        }

        if (tenantContext.Current is null)
        {
            return InvalidRefresh();
        }

        var hash = jwt.HashRefreshToken(request.RefreshToken);
        var existing = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (existing is null)
        {
            return InvalidRefresh();
        }

        var now = time.GetUtcNow().UtcDateTime;
        if (!existing.IsActive(now))
        {
            if (existing.ReplacedById is not null)
            {
                await RevokeChainAsync(db, existing, now, ct);
                await db.SaveChangesAsync(ct);
            }
            return InvalidRefresh();
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == existing.UserId, ct);
        if (user is null || !user.IsActive)
        {
            return InvalidRefresh();
        }

        var (newToken, newHash, newExpiresAt) = jwt.GenerateRefreshToken();
        var replacement = RefreshToken.Issue(existing.TenantId, existing.UserId, newHash, newExpiresAt, time);
        db.RefreshTokens.Add(replacement);
        existing.RevokeAndReplace(replacement.Id, time);
        await db.SaveChangesAsync(ct);

        var access = jwt.IssueAccessToken(user.Id, existing.TenantId, user.Email, user.Role);
        return Results.Ok(new AuthResponse(
            access.Token,
            newToken,
            access.ExpiresInSeconds,
            user.Id.Value,
            existing.TenantId.Value,
            user.Email,
            user.Role.ToString()));
    }

    private static async Task<IResult> LogoutAsync(
        [FromBody] LogoutRequest request,
        IValidator<LogoutRequest> validator,
        AzKotleDbContext db,
        IJwtTokenService jwt,
        TimeProvider time,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.ToDictionary());
        }

        var hash = jwt.HashRefreshToken(request.RefreshToken);
        var existing = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (existing is not null && existing.RevokedAt is null)
        {
            existing.Revoke(time);
            await db.SaveChangesAsync(ct);
        }

        return Results.NoContent();
    }

    private static async Task<AuthResponse> IssueTokensAndSaveAsync(
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

        return new AuthResponse(
            access.Token,
            refreshToken,
            access.ExpiresInSeconds,
            user.Id.Value,
            tenantId.Value,
            user.Email,
            user.Role.ToString());
    }

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

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg && pg.SqlState == UniqueViolationSqlState;

    private static IResult InvalidCredentials() =>
        Results.Json(new { error = "invalid_credentials" }, statusCode: StatusCodes.Status401Unauthorized);

    private static IResult InvalidRefresh() =>
        Results.Json(new { error = "invalid_refresh_token" }, statusCode: StatusCodes.Status401Unauthorized);
}
