using System.Net.Http.Headers;

namespace AzKotle.Web.Client.Auth;

public sealed class JwtAuthHandler : DelegatingHandler
{
    private readonly AuthSession _session;

    public JwtAuthHandler(AuthSession session)
    {
        _session = session;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var tokens = await _session.LoadAsync();
        if (tokens is not null && !string.IsNullOrWhiteSpace(tokens.AccessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
