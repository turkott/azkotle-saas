namespace AzKotle.Web.Client.Auth;

/// <summary>
/// In-memory snapshot autentizovaného stavu klienta. Refresh token tu není —
/// žije v HttpOnly Secure SameSite=Strict cookie spravované backendem
/// (<see cref="AzKotle.Api.Endpoints.AuthEndpoints"/>). JS k němu nemá přístup.
/// AccessToken se po F5 ztratí — <see cref="AuthApiClient.SilentRefreshAsync"/>
/// při bootu zkusí session obnovit přes přeživší cookie.
/// </summary>
public sealed record AuthTokens(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    Guid UserId,
    Guid TenantId,
    string Email,
    string Role,
    string? TenantSlug);
