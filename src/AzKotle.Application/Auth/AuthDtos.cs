namespace AzKotle.Application.Auth;

public sealed record RegisterRequest(
    string Email,
    string Password,
    string FullName,
    string TenantSlug,
    string CompanyName,
    string? Ico);

public sealed record LoginRequest(string Email, string Password, string? TenantSlug = null);

public sealed record RefreshRequest(string RefreshToken, string? TenantSlug = null);

public sealed record LogoutRequest(string RefreshToken);

public sealed record AuthResponse(
    string AccessToken,
    string RefreshToken,
    int ExpiresIn,
    Guid UserId,
    Guid TenantId,
    string Email,
    string Role);
