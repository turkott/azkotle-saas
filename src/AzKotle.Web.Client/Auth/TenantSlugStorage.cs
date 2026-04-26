using Microsoft.JSInterop;

namespace AzKotle.Web.Client.Auth;

/// <summary>
/// Persistuje pouze tenant slug (plain string, ne sensitivní — slug je viditelný
/// v Caddy logu, JWT claimu apod.) v <c>localStorage</c>. Slouží jako vstup pro
/// <see cref="AuthApiClient.SilentRefreshAsync"/> po restartu prohlížeče, kdy
/// access token v paměti je pryč, ale azkotle_refresh cookie přežila (Max-Age 30 d).
/// </summary>
public sealed class TenantSlugStorage
{
    internal const string Key = "azkotle.tenant_slug";

    private readonly IJSRuntime _js;

    public TenantSlugStorage(IJSRuntime js) => _js = js;

    public ValueTask<string?> GetAsync() =>
        _js.InvokeAsync<string?>("localStorage.getItem", Key);

    public ValueTask SetAsync(string slug) =>
        _js.InvokeVoidAsync("localStorage.setItem", Key, slug);

    public ValueTask ClearAsync() =>
        _js.InvokeVoidAsync("localStorage.removeItem", Key);
}
