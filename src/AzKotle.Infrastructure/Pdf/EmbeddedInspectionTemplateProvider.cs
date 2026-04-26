using System.Reflection;
using System.Text.Json;
using AzKotle.Application.Abstractions;
using AzKotle.Domain.Entities.Inspections;

namespace AzKotle.Infrastructure.Pdf;

public sealed class EmbeddedInspectionTemplateProvider : IInspectionTemplateProvider
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly Lazy<InspectionTemplate?> _nv191 = new(
        () => Load("AzKotle.Infrastructure.Templates.nv191_2022.json"),
        LazyThreadSafetyMode.ExecutionAndPublication);

    public InspectionTemplate? GetTemplate(InspectionType type) => type switch
    {
        InspectionType.AnnualNv191 => _nv191.Value,
        _ => null,
    };

    private static InspectionTemplate? Load(string resourceName)
    {
        var assembly = typeof(EmbeddedInspectionTemplateProvider).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            throw new InvalidOperationException(
                $"Embedded template '{resourceName}' not found in assembly '{assembly.FullName}'.");
        }

        return JsonSerializer.Deserialize<InspectionTemplate>(stream, _jsonOptions);
    }
}
