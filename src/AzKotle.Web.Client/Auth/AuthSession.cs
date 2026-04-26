namespace AzKotle.Web.Client.Auth;

/// <summary>
/// In-memory session — žije po dobu life cycle WASM hosta (typicky jeden tab,
/// resetuje se F5). Po F5 silent refresh přes cookie znovu naplní.
/// </summary>
public sealed class AuthSession
{
    private AuthTokens? _cached;

    public AuthTokens? Current => _cached;

    public event Action? Changed;

    /// <summary>
    /// Vrátí aktuální tokens. Async signatura zachována kvůli kompatibilitě
    /// s <see cref="JwtAuthenticationStateProvider"/> a volajícími page komponentami.
    /// </summary>
    public Task<AuthTokens?> LoadAsync() => Task.FromResult(_cached);

    public Task SetAsync(AuthTokens tokens)
    {
        _cached = tokens;
        Changed?.Invoke();
        return Task.CompletedTask;
    }

    public Task ClearAsync()
    {
        _cached = null;
        Changed?.Invoke();
        return Task.CompletedTask;
    }
}
