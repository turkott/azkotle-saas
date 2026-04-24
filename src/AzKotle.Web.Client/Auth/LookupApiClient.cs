using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AzKotle.Application.Abstractions;

namespace AzKotle.Web.Client.Auth;

public sealed class LookupApiClient
{
    private static readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public LookupApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<LookupResult<AresCompany>> LookupAresAsync(string ico, CancellationToken ct = default)
    {
        using var response = await _http.GetAsync($"api/v1/lookup/ares/{ico}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return LookupResult<AresCompany>.NotFound("IČO nebylo v ARES nalezeno.");
        }

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            return LookupResult<AresCompany>.Failure("Neplatné IČO.");
        }

        if (!response.IsSuccessStatusCode)
        {
            return LookupResult<AresCompany>.Failure("ARES je dočasně nedostupný.");
        }

        var company = await response.Content.ReadFromJsonAsync<AresCompany>(_serializerOptions, ct);
        return company is null
            ? LookupResult<AresCompany>.Failure("Prázdná odpověď ARES.")
            : LookupResult<AresCompany>.Ok(company);
    }
}

public sealed record LookupResult<T>(bool Succeeded, T? Value, string? Error, bool IsNotFound)
{
    public static LookupResult<T> Ok(T value) => new(true, value, null, false);
    public static LookupResult<T> NotFound(string message) => new(false, default, message, true);
    public static LookupResult<T> Failure(string message) => new(false, default, message, false);
}
