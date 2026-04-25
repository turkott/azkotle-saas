using System.Net.Http.Json;
using System.Text.Json;
using AzKotle.Web.Client.Inspections;

namespace AzKotle.Web.Client.Api;

public sealed class InspectionSchemaClient
{
    private static readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly Dictionary<string, InspectionFormSchema> _cache = new();

    public InspectionSchemaClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<InspectionFormSchema> LoadAsync(string templateId, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(templateId, out var cached))
        {
            return cached;
        }

        var schema = await _http.GetFromJsonAsync<InspectionFormSchema>(
            $"schemas/{templateId}.json", _serializerOptions, ct)
            ?? throw new InvalidOperationException($"Šablona '{templateId}' se nepodařila načíst.");

        _cache[templateId] = schema;
        return schema;
    }
}
