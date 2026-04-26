namespace AzKotle.Web.Client.Auth;

// Refresh token už NENÍ v AuthTokens — žije v HttpOnly Secure SameSite=Strict cookie
// (azkotle_refresh, Path=/api/v1/auth) spravované backendem. JS k němu nemá přístup.
// Access token zůstává v paměti (BrowserStorage) — Sprint 1 / F3 cookie cutover.
public sealed record AuthTokens(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    Guid UserId,
    Guid TenantId,
    string Email,
    string Role,
    string? TenantSlug);

internal static class AuthStorageKeys
{
    internal const string Tokens = "azkotle.auth";
}
