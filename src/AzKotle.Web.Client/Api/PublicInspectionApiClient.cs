using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AzKotle.Application.PublicAccess;

namespace AzKotle.Web.Client.Api;

public sealed class PublicInspectionApiClient
{
    private static readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public PublicInspectionApiClient(HttpClient http)
    {
        _http = http;
    }

    public Uri BaseAddress => _http.BaseAddress
        ?? throw new InvalidOperationException("PublicInspectionApiClient: BaseAddress not configured.");

    public async Task<PublicInspectionResponse?> GetSummaryAsync(
        string accessHash, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"api/v1/public/inspections/{Uri.EscapeDataString(accessHash)}", ct);
        if (resp.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<PublicInspectionResponse>(_serializerOptions, ct);
    }

    public Uri BuildPdfDownloadUri(string accessHash) =>
        new(BaseAddress, $"api/v1/public/inspections/{Uri.EscapeDataString(accessHash)}/pdf");
}
