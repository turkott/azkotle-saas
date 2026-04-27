using System.Net.Http.Json;
using System.Text.Json;

namespace AzKotle.Web.Client.Api;

public sealed class DashboardApiClient
{
    private static readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public DashboardApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<DashboardSummaryDto?> GetSummaryAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync("api/v1/dashboard/summary", ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<DashboardSummaryDto>(_serializerOptions, ct);
    }

    public async Task<IReadOnlyList<UpcomingExpirationDto>> GetExpirationsAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync("api/v1/dashboard/expirations", ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<List<UpcomingExpirationDto>>(_serializerOptions, ct))
            ?? new List<UpcomingExpirationDto>();
    }
}

public sealed record DashboardSummaryDto(
    string CompanyName,
    int TotalInspections,
    int TotalSignedInspections,
    int TotalActiveTechnicians);

public sealed record UpcomingExpirationDto(
    Guid InspectionId,
    Guid BoilerId,
    string BoilerName,
    Guid CustomerId,
    string CustomerName,
    DateOnly NextDueAt,
    int DaysRemaining);
