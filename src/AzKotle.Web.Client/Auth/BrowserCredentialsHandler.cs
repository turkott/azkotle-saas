using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace AzKotle.Web.Client.Auth;

/// <summary>
/// Vynucuje <see cref="BrowserRequestCredentials.Include"/> na všech requestech.
/// Bez toho WASM Fetch API neposílá ani nepřijímá cookies cross-origin
/// (app.az-kotle.cz → api.az-kotle.cz), což rozbije refresh flow přes
/// HttpOnly azkotle_refresh cookie.
///
/// SetBrowserRequestCredentials uloží volbu na <see cref="HttpRequestMessage.Options"/>,
/// takže přežije i 401 retry uvnitř <see cref="JwtAuthHandler"/>.
/// </summary>
internal sealed class BrowserCredentialsHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
        return base.SendAsync(request, cancellationToken);
    }
}
