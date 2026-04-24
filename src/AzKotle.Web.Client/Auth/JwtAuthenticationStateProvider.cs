using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;

namespace AzKotle.Web.Client.Auth;

public sealed class JwtAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly AuthSession _session;

    public JwtAuthenticationStateProvider(AuthSession session)
    {
        _session = session;
        _session.Changed += OnSessionChanged;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var tokens = await _session.LoadAsync();
        if (tokens is null || string.IsNullOrWhiteSpace(tokens.AccessToken))
        {
            return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
        }

        if (tokens.AccessTokenExpiresAt <= DateTime.UtcNow)
        {
            return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
        }

        var identity = BuildIdentity(tokens);
        return new AuthenticationState(new ClaimsPrincipal(identity));
    }

    private void OnSessionChanged() =>
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());

    private static ClaimsIdentity BuildIdentity(AuthTokens tokens)
    {
        var claims = new List<Claim>();
        var payload = ParseJwtPayload(tokens.AccessToken);
        if (payload is not null)
        {
            foreach (var property in payload.Value.EnumerateObject())
            {
                var value = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Number => property.Value.GetRawText(),
                    JsonValueKind.True or JsonValueKind.False => property.Value.GetBoolean().ToString(),
                    _ => property.Value.GetRawText(),
                };
                if (value is not null)
                {
                    claims.Add(new Claim(property.Name, value));
                }
            }
        }

        if (!claims.Any(c => c.Type == ClaimTypes.Name))
        {
            claims.Add(new Claim(ClaimTypes.Name, tokens.Email));
        }

        if (!claims.Any(c => c.Type == ClaimTypes.Role))
        {
            claims.Add(new Claim(ClaimTypes.Role, tokens.Role));
        }

        return new ClaimsIdentity(claims, authenticationType: "jwt", nameType: ClaimTypes.Name, roleType: ClaimTypes.Role);
    }

    private static JsonElement? ParseJwtPayload(string token)
    {
        var parts = token.Split('.');
        if (parts.Length < 2)
        {
            return null;
        }

        try
        {
            var json = Base64UrlDecode(parts[1]);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2:
                padded += "==";
                break;
            case 3:
                padded += "=";
                break;
        }
        return Convert.FromBase64String(padded);
    }
}
