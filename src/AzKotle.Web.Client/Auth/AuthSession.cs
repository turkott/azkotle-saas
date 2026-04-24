using System.Text.Json;

namespace AzKotle.Web.Client.Auth;

public sealed class AuthSession
{
    private readonly BrowserStorage _storage;
    private AuthTokens? _cached;

    public AuthSession(BrowserStorage storage)
    {
        _storage = storage;
    }

    public AuthTokens? Current => _cached;

    public event Action? Changed;

    public async Task<AuthTokens?> LoadAsync()
    {
        if (_cached is not null)
        {
            return _cached;
        }

        var raw = await _storage.GetAsync(AuthStorageKeys.Tokens);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            _cached = JsonSerializer.Deserialize<AuthTokens>(raw);
        }
        catch (JsonException)
        {
            await _storage.RemoveAsync(AuthStorageKeys.Tokens);
            _cached = null;
        }

        return _cached;
    }

    public async Task SetAsync(AuthTokens tokens)
    {
        _cached = tokens;
        var raw = JsonSerializer.Serialize(tokens);
        await _storage.SetAsync(AuthStorageKeys.Tokens, raw);
        Changed?.Invoke();
    }

    public async Task ClearAsync()
    {
        _cached = null;
        await _storage.RemoveAsync(AuthStorageKeys.Tokens);
        Changed?.Invoke();
    }
}
