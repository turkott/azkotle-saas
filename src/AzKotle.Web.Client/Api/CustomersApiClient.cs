using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AzKotle.Application.Common;
using AzKotle.Application.Customers;

namespace AzKotle.Web.Client.Api;

public sealed class CustomersApiClient
{
    private static readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public CustomersApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<PagedResponse<CustomerDto>> ListAsync(
        string? search = null,
        string? cursor = null,
        int? pageSize = null,
        CancellationToken ct = default)
    {
        var query = ApiQuery.Build(("search", search), ("cursor", cursor), ("pageSize", pageSize?.ToString()));
        var resp = await _http.GetAsync($"api/v1/customers{query}", ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<PagedResponse<CustomerDto>>(_serializerOptions, ct))!;
    }

    public async Task<CustomerDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"api/v1/customers/{id}", ct);
        if (resp.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<CustomerDto>(_serializerOptions, ct);
    }

    public async Task<CustomerDto> CreateAsync(CreateCustomerRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("api/v1/customers", request, _serializerOptions, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CustomerDto>(_serializerOptions, ct))!;
    }

    public async Task<CustomerDto> UpdateAsync(Guid id, UpdateCustomerRequest request, CancellationToken ct = default)
    {
        var resp = await _http.PutAsJsonAsync($"api/v1/customers/{id}", request, _serializerOptions, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CustomerDto>(_serializerOptions, ct))!;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync($"api/v1/customers/{id}", ct);
        return resp.StatusCode == HttpStatusCode.NoContent;
    }
}
