using System.Net.Http.Json;
using System.Text.Json;
using AzKotle.Application.Auth;

namespace AzKotle.Web.Client.Auth;

public sealed class AuthApiClient
{
    private const string AuthPath = "api/v1/auth";
    private static readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly AuthSession _session;

    public AuthApiClient(HttpClient http, AuthSession session)
    {
        _http = http;
        _session = session;
    }

    public async Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        using var response = await _http.PostAsJsonAsync($"{AuthPath}/register", request, _serializerOptions, ct);
        return await ReadResultAsync(response, ct);
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        using var response = await _http.PostAsJsonAsync($"{AuthPath}/login", request, _serializerOptions, ct);
        return await ReadResultAsync(response, ct);
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        var tokens = _session.Current ?? await _session.LoadAsync();
        if (tokens is not null)
        {
            try
            {
                await _http.PostAsJsonAsync(
                    $"{AuthPath}/logout",
                    new LogoutRequest(tokens.RefreshToken),
                    _serializerOptions,
                    ct);
            }
            catch (HttpRequestException)
            {
                // Best-effort logout; local session is still cleared below.
            }
        }

        await _session.ClearAsync();
    }

    private async Task<AuthResult> ReadResultAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            var payload = await response.Content.ReadFromJsonAsync<AuthResponse>(_serializerOptions, ct);
            if (payload is null)
            {
                return AuthResult.Failure("Prázdná odpověď serveru.");
            }

            var tokens = new AuthTokens(
                AccessToken: payload.AccessToken,
                RefreshToken: payload.RefreshToken,
                AccessTokenExpiresAt: DateTime.UtcNow.AddSeconds(payload.ExpiresIn),
                UserId: payload.UserId,
                TenantId: payload.TenantId,
                Email: payload.Email,
                Role: payload.Role,
                TenantSlug: null);

            await _session.SetAsync(tokens);
            return AuthResult.Ok();
        }

        var message = await FormatErrorAsync(response, ct);
        return AuthResult.Failure(message);
    }

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
                    "tenant_required" => "Chybí identifikace firmy (subdoména).",
                    "email_taken" => "Email už je použitý.",
                    "slug_taken" => "Identifikátor firmy už existuje.",
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
