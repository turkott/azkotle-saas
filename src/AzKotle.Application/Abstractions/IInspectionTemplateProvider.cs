using AzKotle.Domain.Entities.Inspections;

namespace AzKotle.Application.Abstractions;

public interface IInspectionTemplateProvider
{
    InspectionTemplate? GetTemplate(InspectionType type);
}

public sealed record InspectionTemplate(
    string Id,
    string Version,
    string Title,
    IReadOnlyList<InspectionTemplateSection> Sections);

public sealed record InspectionTemplateSection(
    string Id,
    string Title,
    IReadOnlyList<InspectionTemplateField> Fields);

public sealed record InspectionTemplateField(
    string Id,
    string Label,
    string Type,
    IReadOnlyList<string>? Options);
