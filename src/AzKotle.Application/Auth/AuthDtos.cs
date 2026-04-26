namespace AzKotle.Application.Auth;

public sealed record RegisterRequest(
    string Email,
    string Password,
    string FullName,
    string TenantSlug,
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
