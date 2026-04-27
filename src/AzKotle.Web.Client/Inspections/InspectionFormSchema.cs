using System.Text.Json.Serialization;

namespace AzKotle.Web.Client.Inspections;

public sealed class InspectionFormSchema
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("version")] public string Version { get; set; } = string.Empty;
    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("sections")] public List<InspectionFormSection> Sections { get; set; } = new();
}

public sealed class InspectionFormSection
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
    [JsonPropertyName("fields")] public List<InspectionFormField> Fields { get; set; } = new();
}

public sealed class InspectionFormField
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("label")] public string Label { get; set; } = string.Empty;
    [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
    [JsonPropertyName("required")] public bool Required { get; set; }
    [JsonPropertyName("options")] public List<string>? Options { get; set; }
    [JsonPropertyName("min")] public decimal? Min { get; set; }
    [JsonPropertyName("max")] public decimal? Max { get; set; }
    [JsonPropertyName("step")] public decimal? Step { get; set; }
}

public static class InspectionFieldTypes
{
    public const string Number = "number";
    public const string Select = "select";
    public const string Boolean = "boolean";
    public const string Date = "date";
    public const string Text = "text";
    public const string Textarea = "textarea";
    public const string Photo = "photo";
    public const string Signature = "signature";
}
