using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AzKotle.Application.Common;
using AzKotle.Application.Locations;

namespace AzKotle.Web.Client.Api;

public sealed class LocationsApiClient
{
    private static readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public LocationsApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<PagedResponse<LocationDto>> ListAsync(
        Guid? customerId = null,
        string? search = null,
        string? cursor = null,
        int? pageSize = null,
        CancellationToken ct = default)
    {
        var query = ApiQuery.Build(
            ("customerId", customerId?.ToString()),
            ("search", search),
            ("cursor", cursor),
            ("pageSize", pageSize?.ToString()));
        var resp = await _http.GetAsync($"api/v1/locations{query}", ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<PagedResponse<LocationDto>>(_serializerOptions, ct))!;
    }

    public async Task<LocationDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"api/v1/locations/{id}", ct);
        if (resp.StatusCode == HttpStatusCode.NotFound)
            return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<LocationDto>(_serializerOptions, ct);
    }

    public async Task<LocationDto> CreateAsync(CreateLocationRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("api/v1/locations", request, _serializerOptions, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<LocationDto>(_serializerOptions, ct))!;
    }

    public async Task<LocationDto> UpdateAsync(Guid id, UpdateLocationRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PutAsJsonAsync($"api/v1/locations/{id}", request, _serializerOptions, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<LocationDto>(_serializerOptions, ct))!;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync($"api/v1/locations/{id}", ct);
        return resp.StatusCode == HttpStatusCode.NoContent;
    }
}
