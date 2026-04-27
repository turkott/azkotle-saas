using System.Net.Http.Json;
using System.Text.Json;
using AzKotle.Application.Auth;

namespace AzKotle.Web.Client.Auth;

/// <summary>
/// HTTP klient pro <c>/api/v1/auth/*</c> endpointy. Používá vlastní
/// <see cref="HttpClient"/> bez <see cref="JwtAuthHandler"/> — důvody:
///  (1) auth endpointy jsou anonymní (žádný Bearer netřeba),
///  (2) odděluje DI graf — JwtAuthHandler injecting AuthApiClient pro
///      401 retry by jinak vyrobil cyklus.
/// Cookies (refresh) jdou přes <see cref="BrowserCredentialsHandler"/>.
/// </summary>
public sealed class AuthApiClient
{
    private const string AuthPath = "api/v1/auth";
    private static readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly AuthSession _session;
    private readonly TenantSlugStorage _slugStorage;

    public AuthApiClient(HttpClient http, AuthSession session, TenantSlugStorage slugStorage)
    {
        _http = http;
        _session = session;
        _slugStorage = slugStorage;
    }

    public async Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        using var response = await _http.PostAsJsonAsync($"{AuthPath}/register", request, _serializerOptions, ct);
        return await ReadAuthResponseAsync(response, request.TenantSlug, ct);
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        using var response = await _http.PostAsJsonAsync($"{AuthPath}/login", request, _serializerOptions, ct);
        return await ReadAuthResponseAsync(response, request.TenantSlug, ct);
    }

    /// <summary>
    /// Pokus o navázání session pouze přes přeživší <c>azkotle_refresh</c> cookie.
    /// Volá se z <see cref="JwtAuthHandler"/> na 401 retry a z bootu v Program.cs.
    /// Vrací <c>true</c> pokud server vydal nový access token, jinak <c>false</c>
    /// (anonymous nebo síťová chyba).
    /// </summary>
    public async Task<bool> SilentRefreshAsync(string? tenantSlug, CancellationToken ct = default)
    {
        try
        {
            var body = new RefreshRequest(tenantSlug);
            using var response = await _http.PostAsJsonAsync($"{AuthPath}/refresh", body, _serializerOptions, ct);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var payload = await response.Content.ReadFromJsonAsync<AuthResponse>(_serializerOptions, ct);
            if (payload is null)
            {
                return false;
            }

            await _session.SetAsync(BuildTokens(payload, tenantSlug));
            return true;
        }
        catch (HttpRequestException)
        {
            // network error / first visit / cookie missing → zůstává anonymous
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// /auth/forgot-password — server vždy vrací 204 (žádný feedback o existenci
    /// emailu), takže "úspěch" pro UX znamená jen "request prošel". Network error
    /// nebo validation se reportuje frontendu.
    /// </summary>
    public async Task<AuthResult> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct = default)
    {
        using var response = await _http.PostAsJsonAsync($"{AuthPath}/forgot-password", request, _serializerOptions, ct);
        if (response.IsSuccessStatusCode)
        {
            return AuthResult.Ok();
        }

        var message = await FormatErrorAsync(response, ct);
        return AuthResult.Failure(message);
    }

    public async Task<AuthResult> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default)
    {
        using var response = await _http.PostAsJsonAsync($"{AuthPath}/reset-password", request, _serializerOptions, ct);
        if (response.IsSuccessStatusCode)
        {
            return AuthResult.Ok();
        }

        var message = await FormatErrorAsync(response, ct);
        return AuthResult.Failure(message);
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        // Refresh token žije v HttpOnly cookie — server si ho přečte sám, žádné body.
        // Server vždy odpoví 204 + Set-Cookie s Max-Age=0 (clear). Pokud network failne,
        // smazat session lokálně i tak — uživatel se chce odhlásit.
        try
        {
            using var _ = await _http.PostAsync($"{AuthPath}/logout", content: null, ct);
        }
        catch (HttpRequestException)
        {
            // best-effort — local session se vyčistí níže
        }
        catch (TaskCanceledException)
        {
        }

        await _session.ClearAsync();
        await _slugStorage.ClearAsync();
    }

    private async Task<AuthResult> ReadAuthResponseAsync(
        HttpResponseMessage response,
        string? requestSlug,
        CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            var payload = await response.Content.ReadFromJsonAsync<AuthResponse>(_serializerOptions, ct);
            if (payload is null)
            {
                return AuthResult.Failure("Prázdná odpověď serveru.");
            }

            await _session.SetAsync(BuildTokens(payload, requestSlug));

            // Persistuj slug pro silent refresh napříč browser restartem.
            if (!string.IsNullOrWhiteSpace(requestSlug))
            {
                await _slugStorage.SetAsync(requestSlug);
            }

            return AuthResult.Ok();
        }

        var message = await FormatErrorAsync(response, ct);
        return AuthResult.Failure(message);
    }

    private static AuthTokens BuildTokens(AuthResponse payload, string? slug) => new(
        AccessToken: payload.AccessToken,
        AccessTokenExpiresAt: DateTime.UtcNow.AddSeconds(payload.ExpiresIn),
        UserId: payload.UserId,
        TenantId: payload.TenantId,
        Email: payload.Email,
        Role: payload.Role,
        TenantSlug: slug);

    private static async Task<string> FormatErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(body))
        {
            return $"Chyba ({(int)response.StatusCode}).";
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                return err.GetString() switch
                {
                    "invalid_credentials" => "Neplatný email nebo heslo.",
                    "invalid_refresh_token" => "Přihlášení vypršelo.",
                    "invalid_reset_token" => "Reset odkaz je neplatný nebo už vypršel. Vyžádejte si nový.",
                    "tenant_required" => "Chybí identifikace firmy (subdoména).",
                    "email_taken" => "Email už je použitý.",
                    "ico_taken" => "Firma s tímto IČO je už zaregistrovaná.",
                    "slug_taken" => "Identifikátor firmy už existuje.",
                    "slug_unavailable" => "Nepodařilo se přiřadit identifikátor firmy. Zkuste prosím jiný název.",
                    _ => err.GetString() ?? body,
                };
            }

            if (doc.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            {
                var messages = new List<string>();
                foreach (var field in errors.EnumerateObject())
                {
                    foreach (var msg in field.Value.EnumerateArray())
                    {
                        var text = msg.GetString();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            messages.Add(text);
                        }
                    }
                }
                if (messages.Count > 0)
                {
                    return string.Join(" ", messages);
                }
            }
        }
        catch (JsonException)
        {
        }

        return $"Chyba ({(int)response.StatusCode}).";
    }
}

public sealed record AuthResult(bool Succeeded, string? Error)
{
    public static AuthResult Ok() => new(true, null);
    public static AuthResult Failure(string error) => new(false, error);
}
