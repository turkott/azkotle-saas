using System.Net;
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
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<InspectionDto>(_serializerOptions, ct))!;
    }

    public async Task<SignedInspectionResponse> SignAsync(Guid id, SignInspectionRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync($"api/v1/inspections/{id}/sign", request, _serializerOptions, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<SignedInspectionResponse>(_serializerOptions, ct))!;
    }

    public async Task<Stream> DownloadPdfAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"api/v1/inspections/{id}/pdf", HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStreamAsync(ct);
    }
}
