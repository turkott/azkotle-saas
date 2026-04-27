using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AzKotle.Api.MultiTenancy;
using AzKotle.Application.Abstractions;
using AzKotle.Application.Auth;
using AzKotle.Domain.Common;
using AzKotle.Domain.Entities.Audit;
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

        group.MapPost("/forgot-password", ForgotPasswordAsync)
            .WithName("AuthForgotPassword")
            .WithMetadata(new AllowAnonymousTenantAttribute())
            .AllowAnonymous();

        group.MapPost("/reset-password", ResetPasswordAsync)
            .WithName("AuthResetPassword")
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

        // ICO uniqueness: pre-check protože unique violation v DB by trigger jen
        // generic catch a server by mis-categorizoval jako email_taken. Race window
        // mezi check a insert je akceptovatelný (jeden user musí retry).
        if (!string.IsNullOrWhiteSpace(request.Ico))
        {
            var icoTaken = await db.Tenants.AsNoTracking()
                .AnyAsync(t => t.Ico == request.Ico, ct);
            if (icoTaken)
            {
                return Results.Conflict(new { error = "ico_taken" });
            }
        }

        // Slug resolution: explicit > auto-generated z CompanyName. Auto-gen ASCII
        // foldne diakritiku, lowercase, nahradí non-alnum pomlčkou. Při kolizi přidá
        // 6-char random suffix a retryuje (max 5×).
        string slug;
        if (!string.IsNullOrWhiteSpace(request.TenantSlug))
        {
            var slugTaken = await db.Tenants.AsNoTracking()
                .AnyAsync(t => t.Slug == request.TenantSlug, ct);
            if (slugTaken)
            {
                return Results.Conflict(new { error = "slug_taken" });
            }
            slug = request.TenantSlug;
        }
        else
        {
            var resolved = await ResolveAutoSlugAsync(db, request.CompanyName, ct);
            if (resolved is null)
            {
                return Results.Conflict(new { error = "slug_unavailable" });
            }
            slug = resolved;
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var tenant = Tenant.Create(slug, request.CompanyName, request.Ico, time);
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

        // F20 audit trail: tenant.registered + user.registered. Píšeme uvnitř
        // transakce — když cokoli selže před tx.CommitAsync, audit log se taky
        // rollbackne (žádný dangling entry pro neexistující tenant).
        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = httpContext.Request.Headers.UserAgent.ToString();
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            userAgent = null;
        }

        var tenantMeta = $$"""{"slug":"{{tenant.Slug}}","company_name":"{{JsonEscape(tenant.CompanyName)}}","ico":{{(tenant.Ico is null ? "null" : $"\"{tenant.Ico}\"")}}}""";
        db.AuditLog.Add(AuditLog.Record(
            tenant.Id, user.Id,
            "tenant.registered", "tenant", tenant.Id.Value,
            ipAddress, userAgent, tenantMeta, time));

        var userMeta = $$"""{"email":"{{JsonEscape(user.Email)}}","full_name":"{{JsonEscape(user.FullName)}}","role":"{{user.Role}}"}""";
        db.AuditLog.Add(AuditLog.Record(
            tenant.Id, user.Id,
            "user.registered", "user", user.Id.Value,
            ipAddress, userAgent, userMeta, time));
        await db.SaveChangesAsync(ct);

        var issued = await IssueTokensAndSaveAsync(db, jwt, tenant.Id, user, time, ct);
        await tx.CommitAsync(ct);

        // Cookie nastavujeme AŽ po commitu — kdyby commit failnul, klient
        // nedostane cookie odkazující na neexistující DB záznam.
        SetRefreshCookie(httpContext, issued.PlainRefreshToken, jwtOptions.Value);
        return Results.Ok(issued.Public);
    }

    private static async Task<string?> ResolveAutoSlugAsync(
        AzKotleDbContext db, string companyName, CancellationToken ct)
    {
        var basis = SlugifyCompanyName(companyName);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var candidate = attempt == 0
                ? basis
                : $"{basis[..Math.Min(basis.Length, Tenant.SlugMaxLength - 7)]}-{Guid.NewGuid().ToString("N")[..6]}";
            var taken = await db.Tenants.AsNoTracking().AnyAsync(t => t.Slug == candidate, ct);
            if (!taken)
            {
                return candidate;
            }
        }
        return null;
    }

    private static string SlugifyCompanyName(string companyName)
    {
        // ASCII fold (Czech "Žluťoučký" → "Zlutoucky"), pak lowercase + non-alnum → "-".
        var normalized = companyName.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
            else if (sb.Length > 0 && sb[^1] != '-')
            {
                sb.Append('-');
            }
        }
        var trimmed = sb.ToString().Trim('-');
        if (trimmed.Length == 0)
            trimmed = "firma";
        // Min 2 znaky kvůli existujícímu slug regexu (^[a-z0-9](...)?$).
        if (trimmed.Length == 1)
            trimmed += "0";
        if (trimmed.Length > Tenant.SlugMaxLength)
            trimmed = trimmed[..Tenant.SlugMaxLength];
        return trimmed;
    }

    private static string JsonEscape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");

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

        // Transakce udržuje jednu connection napříč set_config + queries — bez ní
        // by EF Core otevřel/zavřel novou connection pro každý command, interceptor
        // by reset tenant context na empty a RLS-protected query (users/refresh_tokens)
        // by vrátila 0 řádků. Stejný důvod jako u RegisterAsync.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

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
        await tx.CommitAsync(ct);

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

        // Transakce udržuje jednu connection napříč set_config a všemi RLS-protected
        // queries — bez ní by se mezi commands renderovala nová connection a interceptor
        // by smazal tenant context (token hash by se "ztratil" RLS deny-all filtrem).
        await using var tx = await db.Database.BeginTransactionAsync(ct);

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
                // Commit aby chain revocation přežila return v transakci.
                await tx.CommitAsync(ct);
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
        await tx.CommitAsync(ct);

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

    /// <summary>
    /// /auth/forgot-password — vždy 204, ať email existuje nebo ne. Útočník
    /// tak nemůže přes timing rozdíl detekovat platné účty (enumeration).
    /// Email odesíláme jen když najdeme aktivního usera; reset link míří na
    /// {App.BaseUrl}/reset-password?token={plain}&amp;slug={tenant_slug}.
    /// Token žije 1 h a je single-use (used_at).
    /// </summary>
    private static async Task<IResult> ForgotPasswordAsync(
        [FromBody] ForgotPasswordRequest request,
        IValidator<ForgotPasswordRequest> validator,
        AzKotleDbContext db,
        IEmailSender email,
        IOptions<AppOptions> appOptions,
        HttpContext httpContext,
        TimeProvider time,
        ILogger<PasswordResetScope> logger,
        CancellationToken ct)
    {
        // Constant-time defense: všechny větve čekají min 2s před návratem,
        // aby útočník nedokázal přes timing rozeznat existenci účtu/tenanta.
        // (Empirie 2026-04-27: existující 1.3s, neexistující email 0.12s,
        // neexistující tenant 0.07s — měřitelný leak. Padding sjednotí.)
        var minimumWait = Task.Delay(TimeSpan.FromMilliseconds(2000), ct);

        try
        {
            await DoForgotPasswordWorkAsync(request, validator, db, email, appOptions, httpContext, time, logger, ct);
        }
        catch (Exception ex)
        {
            // Nikdy nevracíme 500 — leak by signalizoval, že request došel "dál"
            // (existující tenant/email vs. neexistující). Logujeme a vrátíme 204
            // jako všechny ostatní cesty.
            logger.LogError(ex, "Forgot password unexpected error");
        }

        try
        { await minimumWait; }
        catch (OperationCanceledException) { }
        return Results.NoContent();
    }

    private static async Task DoForgotPasswordWorkAsync(
        ForgotPasswordRequest request,
        IValidator<ForgotPasswordRequest> validator,
        AzKotleDbContext db,
        IEmailSender email,
        IOptions<AppOptions> appOptions,
        HttpContext httpContext,
        TimeProvider time,
        ILogger<PasswordResetScope> logger,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            // Validační chyba — minimumWait stejně doběhne, attacker dostane 204
            // jako u úspěchu. Validace je tu jen jako defense in depth.
            return;
        }

        var tenantId = await db.Tenants.AsNoTracking()
            .Where(t => t.Slug == request.TenantSlug)
            .Select(t => (Guid?)t.Id.Value)
            .FirstOrDefaultAsync(ct);

        if (tenantId is null)
        {
            return;
        }

        var resolvedTenantId = new TenantId(tenantId.Value);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync(
            "SELECT set_config('app.current_tenant_id', {0}, false)",
            new object[] { resolvedTenantId.Value.ToString() },
            ct);

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail, ct);

        if (user is null || !user.IsActive || user.TenantId != resolvedTenantId)
        {
            await tx.CommitAsync(ct);
            return;
        }

        var plainToken = GeneratePlainToken();
        var tokenHash = HashToken(plainToken);
        var now = time.GetUtcNow().UtcDateTime;
        var expiresAt = now.AddHours(1);

        var resetToken = PasswordResetToken.Issue(resolvedTenantId, user.Id, tokenHash, expiresAt, time);
        db.PasswordResetTokens.Add(resetToken);

        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = httpContext.Request.Headers.UserAgent.ToString();
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            userAgent = null;
        }

        var requestMeta = $$"""{"email":"{{JsonEscape(user.Email)}}"}""";
        db.AuditLog.Add(AuditLog.Record(
            resolvedTenantId, user.Id,
            "password.reset_requested", "user", user.Id.Value,
            ipAddress, userAgent, requestMeta, time));

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        // Email send má tight timeout (1.5s) — kdyby Resend tonul, vrátíme 204
        // pořád v 2s minimumWait okně. Email může být doručen i tak (Resend
        // accept request rychle, doručení je async na jejich straně).
        var resetUrl = BuildResetUrl(appOptions.Value.BaseUrl, plainToken, request.TenantSlug);
        using var emailCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        emailCts.CancelAfter(TimeSpan.FromMilliseconds(1500));
        try
        {
            await email.SendAsync(BuildResetEmail(user.Email, user.FullName, resetUrl), emailCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Reset email send timeout for userId={UserId}", user.Id.Value);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Reset email send failed for userId={UserId}", user.Id.Value);
        }
    }

    /// <summary>
    /// /auth/reset-password — validuje token + tenant, hashne SHA256, hledá
    /// PasswordResetToken s match a IsRedeemable. Po úspěchu označí token
    /// MarkUsed a updatuje password. NEvrací JWT — uživatel se musí explicitně
    /// přihlásit přes /login (potvrzení vlastnictví hesla).
    /// </summary>
    private static async Task<IResult> ResetPasswordAsync(
        [FromBody] ResetPasswordRequest request,
        IValidator<ResetPasswordRequest> validator,
        AzKotleDbContext db,
        IPasswordHasher hasher,
        HttpContext httpContext,
        TimeProvider time,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.ToDictionary());
        }

        var tenantId = await db.Tenants.AsNoTracking()
            .Where(t => t.Slug == request.TenantSlug)
            .Select(t => (Guid?)t.Id.Value)
            .FirstOrDefaultAsync(ct);

        if (tenantId is null)
        {
            return InvalidResetToken();
        }

        var resolvedTenantId = new TenantId(tenantId.Value);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync(
            "SELECT set_config('app.current_tenant_id', {0}, false)",
            new object[] { resolvedTenantId.Value.ToString() },
            ct);

        var tokenHash = HashToken(request.Token);
        var resetToken = await db.PasswordResetTokens
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

        var now = time.GetUtcNow().UtcDateTime;
        if (resetToken is null || !resetToken.IsRedeemable(now))
        {
            return InvalidResetToken();
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == resetToken.UserId, ct);
        if (user is null || !user.IsActive)
        {
            return InvalidResetToken();
        }

        user.SetPassword(hasher.Hash(request.NewPassword));
        resetToken.MarkUsed(time);

        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = httpContext.Request.Headers.UserAgent.ToString();
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            userAgent = null;
        }

        db.AuditLog.Add(AuditLog.Record(
            resolvedTenantId, user.Id,
            "password.reset_completed", "user", user.Id.Value,
            ipAddress, userAgent, metadataJson: null, time));

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Results.NoContent();
    }

    private static string GeneratePlainToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Base64UrlEncode(bytes);
    }

    private static string HashToken(string plain)
    {
        var bytes = Encoding.UTF8.GetBytes(plain);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes)
    {
        var encoded = Convert.ToBase64String(bytes);
        return encoded.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string BuildResetUrl(string baseUrl, string token, string tenantSlug)
    {
        var trimmed = string.IsNullOrWhiteSpace(baseUrl)
            ? "https://www.az-kotle.cz"
            : baseUrl.TrimEnd('/');
        return $"{trimmed}/reset-password?token={Uri.EscapeDataString(token)}&slug={Uri.EscapeDataString(tenantSlug)}";
    }

    private static EmailMessage BuildResetEmail(string toAddress, string fullName, string resetUrl)
    {
        var safeName = string.IsNullOrWhiteSpace(fullName) ? "uživateli" : fullName;
        var subject = "Reset hesla — AZ KOTLE";
        var html = $$"""
            <!DOCTYPE html>
            <html lang="cs">
            <body style="font-family: -apple-system, Segoe UI, Roboto, sans-serif; color: #1a1a1a; max-width: 560px; margin: 0 auto; padding: 24px;">
              <h2 style="color: #0d6efd;">Reset hesla</h2>
              <p>Dobrý den, <strong>{{safeName}}</strong>,</p>
              <p>obdrželi jsme žádost o reset hesla k vašemu účtu AZ KOTLE. Pro nastavení nového hesla klikněte na odkaz níže:</p>
              <p style="margin: 24px 0;">
                <a href="{{resetUrl}}" style="background: #0d6efd; color: white; padding: 12px 24px; text-decoration: none; border-radius: 6px; display: inline-block;">Nastavit nové heslo</a>
              </p>
              <p>Odkaz je platný <strong>1 hodinu</strong> a lze ho použít pouze jednou.</p>
              <p>Pokud jste o reset hesla nežádali, tento email ignorujte.</p>
              <hr style="border: none; border-top: 1px solid #e5e5e5; margin: 32px 0 16px;" />
              <p style="font-size: 12px; color: #6c757d;">
                AZ KOTLE — Revize plynových kotlů.<br />
                Tento email je automatický, neodpovídejte na něj.
              </p>
            </body>
            </html>
            """;
        var text = $"""
            Reset hesla — AZ KOTLE

            Dobrý den, {safeName},

            obdrželi jsme žádost o reset hesla k vašemu účtu AZ KOTLE.
            Pro nastavení nového hesla otevřete tento odkaz:

            {resetUrl}

            Odkaz je platný 1 hodinu a lze ho použít pouze jednou.
            Pokud jste o reset hesla nežádali, tento email ignorujte.

            AZ KOTLE — Revize plynových kotlů.
            """;
        return new EmailMessage(toAddress, subject, html, text);
    }

    private static IResult InvalidResetToken() =>
        Results.Json(new { error = "invalid_reset_token" }, statusCode: StatusCodes.Status400BadRequest);

    private static IResult InvalidCredentials() =>
        Results.Json(new { error = "invalid_credentials" }, statusCode: StatusCodes.Status401Unauthorized);

    private static IResult InvalidRefresh() =>
        Results.Json(new { error = "invalid_refresh_token" }, statusCode: StatusCodes.Status401Unauthorized);

    /// <summary>Marker typ pro <see cref="ILogger{T}"/> kategorii v password reset endpoints.</summary>
    internal sealed class PasswordResetScope { }

    /// <summary>Marker typ pro <see cref="ILogger{T}"/> kategorii v RefreshAsync.</summary>
    internal sealed class RefreshTokenScope { }

    /// <summary>Interní výsledek <see cref="IssueTokensAndSaveAsync"/> — Public jde do response body, PlainRefreshToken jde do HttpOnly cookie.</summary>
    private sealed record IssuedTokens(AuthResponse Public, string PlainRefreshToken);
}
