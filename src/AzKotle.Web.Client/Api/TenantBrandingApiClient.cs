using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace AzKotle.Web.Client.Api;

public sealed class TenantBrandingApiClient
{
    private static readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public TenantBrandingApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<TenantBrandingResponse> UploadLogoAsync(
        Stream content,
        string fileName,
        string contentType,
        long contentLength,
        CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        var streamContent = new StreamContent(content);
        streamContent.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        streamContent.Headers.ContentLength = contentLength;
        form.Add(streamContent, "file", fileName);

        var resp = await _http.PostAsync("api/v1/tenant/branding/logo", form, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<TenantBrandingResponse>(_serializerOptions, ct))!;
    }
}

public sealed record TenantBrandingResponse(string LogoStorageKey, DateTime LogoUpdatedAt);
