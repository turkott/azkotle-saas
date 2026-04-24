namespace AzKotle.Web.Client.Auth;

public sealed record AuthTokens(
    string AccessToken,
    string RefreshToken,
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
