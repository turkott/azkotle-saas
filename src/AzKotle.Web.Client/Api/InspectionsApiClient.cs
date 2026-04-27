using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AzKotle.Application.Common;
using AzKotle.Application.Inspections;
using AzKotle.Domain.Entities.Inspections;

namespace AzKotle.Web.Client.Api;

public sealed class InspectionsApiClient
{
    private static readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public InspectionsApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<PagedResponse<InspectionDto>> ListAsync(
        Guid? boilerId = null,
        InspectionStatus? status = null,
        string? cursor = null,
        int? pageSize = null,
        CancellationToken ct = default)
    {
        var query = ApiQuery.Build(
            ("boilerId", boilerId?.ToString()),
            ("status", status?.ToString()),
            ("cursor", cursor),
            ("pageSize", pageSize?.ToString()));
        var resp = await _http.GetAsync($"api/v1/inspections{query}", ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<PagedResponse<InspectionDto>>(_serializerOptions, ct))!;
    }

    public async Task<InspectionDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"api/v1/inspections/{id}", ct);
        if (resp.StatusCode == HttpStatusCode.NotFound)
            return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<InspectionDto>(_serializerOptions, ct);
    }

    public async Task<InspectionDto> CreateAsync(CreateInspectionRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("api/v1/inspections", request, _serializerOptions, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<InspectionDto>(_serializerOptions, ct))!;
    }

    public async Task<InspectionDto> UpdateDraftAsync(Guid id, UpdateInspectionDraftRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PutAsJsonAsync($"api/v1/inspections/{id}/draft", request, _serializerOptions, ct);
        if (resp.StatusCode == HttpStatusCode.Conflict)
        {
            throw new StaleVersionException("Revize má novější verzi v DB.");
        }
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<InspectionDto>(_serializerOptions, ct))!;
    }

    public async Task<SignedInspectionResponse> SignAsync(Guid id, SignInspectionRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync($"api/v1/inspections/{id}/sign", request, _serializerOptions, ct);
        if (resp.StatusCode == HttpStatusCode.Conflict)
        {
            throw new StaleVersionException("Revize má novější verzi v DB.");
        }
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<SignedInspectionResponse>(_serializerOptions, ct))!;
    }

    public async Task<Stream> DownloadPdfAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"api/v1/inspections/{id}/pdf", HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStreamAsync(ct);
    }

    public async Task<UploadPhotoResponse> UploadPhotoAsync(
        Guid id,
        string fieldId,
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
        form.Add(new StringContent(fieldId), "fieldId");

        var resp = await _http.PostAsync($"api/v1/inspections/{id}/photos", form, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var detail = await TryReadErrorAsync(resp, ct);
            return new UploadPhotoResponse(null, detail ?? $"Upload selhal ({(int)resp.StatusCode}).");
        }
        var payload = await resp.Content.ReadFromJsonAsync<UploadPhotoBody>(_serializerOptions, ct);
        return new UploadPhotoResponse(payload?.StorageKey, null);
    }

    /// <summary>
    /// Build a relative URL the browser can use directly as &lt;img src&gt;. Returns
    /// a path on the API base address; the API responds 302 to a presigned S3 URL.
    /// </summary>
    public Uri BuildPhotoUri(Guid id, string storageKey)
    {
        var baseAddr = _http.BaseAddress
            ?? throw new InvalidOperationException("InspectionsApiClient: BaseAddress not configured.");
        var path = $"api/v1/inspections/{id}/photos?storageKey={Uri.EscapeDataString(storageKey)}";
        return new Uri(baseAddr, path);
    }

    private static async Task<string?> TryReadErrorAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        try
        {
            var body = await resp.Content.ReadFromJsonAsync<ErrorBody>(_serializerOptions, ct);
            return body?.Detail ?? body?.Error;
        }
        catch
        {
            return null;
        }
    }

    private sealed record UploadPhotoBody(string StorageKey);
    private sealed record ErrorBody(string? Error, string? Detail);
}

public sealed record UploadPhotoResponse(string? StorageKey, string? Error)
{
    public bool Succeeded => StorageKey is not null;
}

public sealed class StaleVersionException : Exception
{
    public StaleVersionException(string message) : base(message) { }
}
