using System.Globalization;
using System.Text.Json;
using AzKotle.Application.Abstractions;
using AzKotle.Domain.Entities.Inspections;

namespace AzKotle.Application.Inspections;

public sealed class FormSectionMapper
{
    private const string PhotoAttached = "Připojeno";
    private const string EmptyMark = "—";

    private readonly IInspectionTemplateProvider _templates;

    public FormSectionMapper(IInspectionTemplateProvider templates)
    {
        _templates = templates;
    }

    public IReadOnlyList<InspectionReportSection> Map(InspectionType type, string? formDataJson)
    {
        if (string.IsNullOrWhiteSpace(formDataJson) || formDataJson == "{}")
        {
            return Array.Empty<InspectionReportSection>();
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(formDataJson);
        }
        catch (JsonException)
        {
            return Array.Empty<InspectionReportSection>();
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Array.Empty<InspectionReportSection>();
            }

            var template = _templates.GetTemplate(type);
            return template is null
                ? FlatFallback(doc.RootElement)
                : MapBySchema(template, doc.RootElement);
        }
    }

    private static IReadOnlyList<InspectionReportSection> MapBySchema(
        InspectionTemplate template, JsonElement root)
    {
        var sections = new List<InspectionReportSection>(template.Sections.Count);
        foreach (var section in template.Sections)
        {
            var fields = new List<InspectionReportField>(section.Fields.Count);
            foreach (var field in section.Fields)
            {
                if (string.Equals(field.Type, "signature", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                fields.Add(new InspectionReportField(
                    Label: field.Label,
                    DisplayValue: FormatField(field, root)));
            }
            if (fields.Count > 0)
            {
                sections.Add(new InspectionReportSection(section.Title, fields));
            }
        }
        return sections;
    }

    private static IReadOnlyList<InspectionReportSection> FlatFallback(JsonElement root)
    {
        var fields = new List<InspectionReportField>();
        foreach (var prop in root.EnumerateObject())
        {
            fields.Add(new InspectionReportField(
                Label: HumanizeKey(prop.Name),
                DisplayValue: FormatRawValue(prop.Value)));
        }
        return fields.Count == 0
            ? Array.Empty<InspectionReportSection>()
            : new[] { new InspectionReportSection("Vyplněné údaje", fields) };
    }

    private static string FormatField(InspectionTemplateField field, JsonElement root)
    {
        if (!root.TryGetProperty(field.Id, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return EmptyMark;
        }

        if (string.Equals(field.Type, "photo", StringComparison.OrdinalIgnoreCase))
        {
            return IsNonEmptyValue(value) ? PhotoAttached : EmptyMark;
        }

        if (string.Equals(field.Type, "boolean", StringComparison.OrdinalIgnoreCase))
        {
            return value.ValueKind switch
            {
                JsonValueKind.True => "Ano",
                JsonValueKind.False => "Ne",
                _ => EmptyMark,
            };
        }

        if (string.Equals(field.Type, "date", StringComparison.OrdinalIgnoreCase)
            && value.ValueKind == JsonValueKind.String
            && DateOnly.TryParse(value.GetString(), CultureInfo.InvariantCulture, out var date))
        {
            return date.ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("cs-CZ"));
        }

        return FormatRawValue(value);
    }

    private static bool IsNonEmptyValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => !string.IsNullOrWhiteSpace(value.GetString()),
        JsonValueKind.Array => value.GetArrayLength() > 0,
        JsonValueKind.Object => value.EnumerateObject().Any(),
        JsonValueKind.Null => false,
        _ => true,
    };

    private static string FormatRawValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "Ano",
        JsonValueKind.False => "Ne",
        JsonValueKind.Null => EmptyMark,
        _ => value.GetRawText(),
    };

    private static string HumanizeKey(string key)
    {
        var trimmed = key.Replace('_', ' ').Trim();
        return trimmed.Length switch
        {
            0 => key,
            1 => trimmed.ToUpperInvariant(),
            _ => char.ToUpperInvariant(trimmed[0]) + trimmed[1..],
        };
    }
}
