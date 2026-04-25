using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AzKotle.Application.Boilers;
using AzKotle.Application.Common;

namespace AzKotle.Web.Client.Api;

public sealed class BoilersApiClient
{
    private static readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public BoilersApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<PagedResponse<BoilerDto>> ListAsync(
        Guid? locationId = null,
        string? search = null,
        string? cursor = null,
        int? pageSize = null,
        CancellationToken ct = default)
    {
        var query = ApiQuery.Build(
            ("locationId", locationId?.ToString()),
            ("search", search),
            ("cursor", cursor),
            ("pageSize", pageSize?.ToString()));
        var resp = await _http.GetAsync($"api/v1/boilers{query}", ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<PagedResponse<BoilerDto>>(_serializerOptions, ct))!;
    }

    public async Task<BoilerDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"api/v1/boilers/{id}", ct);
        if (resp.StatusCode == HttpStatusCode.NotFound)
            return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<BoilerDto>(_serializerOptions, ct);
    }

    public async Task<BoilerDto> CreateAsync(CreateBoilerRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("api/v1/boilers", request, _serializerOptions, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<BoilerDto>(_serializerOptions, ct))!;
    }

    public async Task<BoilerDto> UpdateAsync(Guid id, UpdateBoilerRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PutAsJsonAsync($"api/v1/boilers/{id}", request, _serializerOptions, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<BoilerDto>(_serializerOptions, ct))!;
    }

    public async Task<BoilerDto> RecordInspectionAsync(Guid id, RecordInspectionRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync($"api/v1/boilers/{id}/inspections", request, _serializerOptions, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<BoilerDto>(_serializerOptions, ct))!;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync($"api/v1/boilers/{id}", ct);
        return resp.StatusCode == HttpStatusCode.NoContent;
    }

    public string QrLabelPdfUrl(Guid id, int copies = 4) =>
        $"{_http.BaseAddress}api/v1/boilers/{id}/qr-label?copies={copies}";

    public string QrPngUrl(Guid id) =>
        $"{_http.BaseAddress}api/v1/boilers/{id}/qr.png";
}
