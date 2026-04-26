using AzKotle.Domain.Entities.Inspections;

namespace AzKotle.Application.Abstractions;

public interface IInspectionReportPdfRenderer
{
    byte[] Render(InspectionReportData data);
}

public sealed record InspectionReportData(
    string InspectionNumber,
    InspectionType Type,
    DateTime PerformedAt,
    DateOnly? NextDueAt,
    InspectionReportTenant Tenant,
    InspectionReportTechnician Technician,
    InspectionReportCustomer Customer,
    InspectionReportLocation Location,
    InspectionReportBoiler Boiler,
    IReadOnlyList<InspectionReportSection> FormSections,
    string? Findings,
    string? Recommendations,
    byte[]? SignatureImage = null);

public sealed record InspectionReportTenant(
    string CompanyName,
    string? Ico,
    string? Dic);

public sealed record InspectionReportTechnician(
    string FullName,
    string? LicenseNo);

public sealed record InspectionReportCustomer(
    string Name,
    string? Ico,
    string? Email,
    string? Phone);

public sealed record InspectionReportLocation(
    string Street,
    string City,
    string Zip);

public sealed record InspectionReportBoiler(
    string QrCode,
    string Manufacturer,
    string Model,
    string SerialNo,
    decimal OutputKw,
    string FuelType,
    DateOnly InstalledAt);

public sealed record InspectionReportSection(
    string Title,
    IReadOnlyList<InspectionReportField> Fields);

public sealed record InspectionReportField(
    string Label,
    string DisplayValue);
