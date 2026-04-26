using System.Net;
using System.Net.Http.Headers;

namespace AzKotle.Web.Client.Auth;

/// <summary>
/// DelegatingHandler pro CRUD api klienty:
///  (1) Připojí <c>Authorization: Bearer &lt;access&gt;</c> z <see cref="AuthSession"/>.
///  (2) Pokud server vrátí 401 a request není auth endpoint, zkusí silent refresh
///      přes <see cref="AuthApiClient"/> a zopakuje původní request s novým bearerem.
///  (3) Concurrent refresh deduplikuje přes <see cref="_refreshLock"/> — první request
///      udělá refresh, ostatní jen čekají a převezmou nový token.
///
/// Cookies (refresh) se posílají díky <see cref="BrowserCredentialsHandler"/>, který
/// je v handler chain před tímto handlerem.
/// </summary>
public sealed class JwtAuthHandler : DelegatingHandler
{
    // Static lock — sdílený napříč všemi instancemi handleru (transient registrace
    // znamená nový handler per CRUD HttpClient). Refresh ovlivňuje globální AuthSession,
    // takže lock musí být globální.
    private static readonly SemaphoreSlim _refreshLock = new(1, 1);

    private readonly AuthSession _session;
    private readonly AuthApiClient _authClient;
    private readonly TenantSlugStorage _slugStorage;

    public JwtAuthHandler(AuthSession session, AuthApiClient authClient, TenantSlugStorage slugStorage)
    {
        _session = session;
        _authClient = authClient;
        _slugStorage = slugStorage;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // Buffer request content — pokud potřebujeme retry po 401 refreshi,
        // HttpContent by jinak byl už spotřebovaný (one-shot stream).
        if (request.Content is not null)
        {
            await request.Content.LoadIntoBufferAsync(cancellationToken);
        }

        var tokens = _session.Current;
        ApplyBearer(request, tokens?.AccessToken);

        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode != HttpStatusCode.Unauthorized || tokens is null)
        {
            return response;
        }

        // Defense in depth — neretrahovat na auth endpointech (AuthApiClient
        // je už nepoužívá, ale kdyby v budoucnu někdo přidal CRUD endpoint pod
        // /auth/, smyčka by byla nepříjemná).
        if (request.RequestUri?.AbsolutePath.Contains("/api/v1/auth/", StringComparison.Ordinal) == true)
        {
            return response;
        }

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            // Mezitím mohl jiný paralelní request už refresh provést. Pokud session
            // má jiný access token než ten, se kterým jsme začali, refresh skipni.
            var current = _session.Current;
            var stillStale = current is null || string.Equals(current.AccessToken, tokens.AccessToken, StringComparison.Ordinal);

            if (stillStale)
            {
                var slug = await _slugStorage.GetAsync();
                var refreshed = await _authClient.SilentRefreshAsync(slug, cancellationToken);
                if (!refreshed)
                {
                    // Refresh selhal — vrátíme původní 401 klientovi (typicky redirect na /login)
                    return response;
                }
            }

            var fresh = _session.Current;
            if (fresh is null)
            {
                return response;
            }

            response.Dispose();

            // Reset bearer hlavičky — content už byl bufferovaný, request se může znovu poslat.
            ApplyBearer(request, fresh.AccessToken);
            return await base.SendAsync(request, cancellationToken);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private static void ApplyBearer(HttpRequestMessage request, string? accessToken)
    {
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }
        else
        {
            request.Headers.Authorization = null;
        }
    }
}
