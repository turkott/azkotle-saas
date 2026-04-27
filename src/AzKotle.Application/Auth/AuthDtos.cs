namespace AzKotle.Application.Auth;

public sealed record RegisterRequest(
    string Email,
    string Password,
    string FullName,
    string? TenantSlug,
    string CompanyName,
    string? Ico);

public sealed record LoginRequest(string Email, string Password, string? TenantSlug = null);

/// <summary>
/// /auth/refresh tělo — refresh token sám o sobě se posílá v HttpOnly Secure
/// SameSite=Strict cookie (azkotle_refresh, Path=/api/v1/auth). Body slouží
/// jen k volitelnému tenantSlug fallbacku, když není dostupný JWT ani subdoména.
/// </summary>
public sealed record RefreshRequest(string? TenantSlug = null);

public sealed record AuthResponse(
    string AccessToken,
    int ExpiresIn,
    Guid UserId,
    Guid TenantId,
    string Email,
    string Role);

/// <summary>
/// /auth/forgot-password tělo. Tenant slug je povinný — bez něj server nemůže
/// určit, komu reset link poslat (multi-tenant: stejný email může existovat
/// pod více tenanty s různými hesly).
/// </summary>
public sealed record ForgotPasswordRequest(string Email, string TenantSlug);

/// <summary>
/// /auth/reset-password tělo. Token je plain (klient ho dostal v emailu);
/// server si ho hashne SHA256 a hledá v DB. TenantSlug je nutný pro
/// resolution tenant kontextu (RLS) — frontend ho dostane stejnou cestou jako
/// token (query parametry v reset linku).
/// </summary>
public sealed record ResetPasswordRequest(string Token, string TenantSlug, string NewPassword);
