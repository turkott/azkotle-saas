using Microsoft.JSInterop;

namespace AzKotle.Web.Client.Auth;

public sealed class BrowserStorage
{
    private readonly IJSRuntime _js;

    public BrowserStorage(IJSRuntime js)
    {
        _js = js;
    }

    public ValueTask<string?> GetAsync(string key) =>
        _js.InvokeAsync<string?>("localStorage.getItem", key);

    public ValueTask SetAsync(string key, string value) =>
        _js.InvokeVoidAsync("localStorage.setItem", key, value);

    public ValueTask RemoveAsync(string key) =>
        _js.InvokeVoidAsync("localStorage.removeItem", key);
}
