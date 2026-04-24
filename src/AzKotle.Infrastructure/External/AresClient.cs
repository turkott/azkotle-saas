using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AzKotle.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace AzKotle.Infrastructure.External;

public sealed class AresClient : IAresClient
{
    public const string HttpClientName = "ares";

    private readonly HttpClient _http;
    private readonly ILogger<AresClient> _logger;

    public AresClient(HttpClient http, ILogger<AresClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<AresCompany?> LookupByIcoAsync(string ico, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ico) || ico.Length != 8 || !ico.All(char.IsDigit))
        {
            return null;
        }

        using var response = await _http.GetAsync(
            $"ekonomicke-subjekty-v-be/rest/ekonomicke-subjekty/{ico}",
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogInformation("ARES lookup for {Ico} returned 404", ico);
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("ARES lookup for {Ico} failed with {Status}", ico, response.StatusCode);
            response.EnsureSuccessStatusCode();
        }

        var payload = await response.Content.ReadFromJsonAsync<AresEkonomickySubjekt>(cancellationToken);
        if (payload is null || string.IsNullOrWhiteSpace(payload.ObchodniJmeno))
        {
            return null;
        }

        return new AresCompany(
            Ico: payload.Ico ?? ico,
            Dic: payload.Dic,
            CompanyName: payload.ObchodniJmeno!.Trim(),
            Street: FormatStreet(payload.Sidlo),
            City: payload.Sidlo?.NazevObce,
            Zip: payload.Sidlo?.Psc?.ToString());
    }

    private static string? FormatStreet(AresSidlo? sidlo)
    {
        if (sidlo is null)
        {
            return null;
        }

        var street = !string.IsNullOrWhiteSpace(sidlo.NazevUlice) ? sidlo.NazevUlice : sidlo.NazevCastiObce;
        var number = sidlo.CisloDomovni is > 0 ? sidlo.CisloDomovni.ToString() : null;
        var orientation = !string.IsNullOrWhiteSpace(sidlo.CisloOrientacni) ? sidlo.CisloOrientacni : null;

        if (string.IsNullOrWhiteSpace(street))
        {
            return null;
        }

        var numberPart = (number, orientation) switch
        {
            (null, null) => string.Empty,
            (_, null) => $" {number}",
            (null, _) => $" {orientation}",
            _ => $" {number}/{orientation}",
        };

        return $"{street}{numberPart}".Trim();
    }

    private sealed record AresEkonomickySubjekt(
        [property: JsonPropertyName("ico")] string? Ico,
        [property: JsonPropertyName("dic")] string? Dic,
        [property: JsonPropertyName("obchodniJmeno")] string? ObchodniJmeno,
        [property: JsonPropertyName("sidlo")] AresSidlo? Sidlo);

    private sealed record AresSidlo(
        [property: JsonPropertyName("nazevUlice")] string? NazevUlice,
        [property: JsonPropertyName("nazevCastiObce")] string? NazevCastiObce,
        [property: JsonPropertyName("nazevObce")] string? NazevObce,
        [property: JsonPropertyName("psc")] int? Psc,
        [property: JsonPropertyName("cisloDomovni")] int? CisloDomovni,
        [property: JsonPropertyName("cisloOrientacni"), JsonConverter(typeof(FlexibleStringConverter))] string? CisloOrientacni);

    private sealed class FlexibleStringConverter : JsonConverter<string?>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType switch
            {
                JsonTokenType.String => reader.GetString(),
                JsonTokenType.Number when reader.TryGetInt64(out var i) => i.ToString(),
                JsonTokenType.Number => reader.GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture),
                JsonTokenType.Null => null,
                _ => null,
            };

        public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value);
    }
}
