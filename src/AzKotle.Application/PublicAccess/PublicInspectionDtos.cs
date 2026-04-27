using AzKotle.Domain.Entities.Inspections;

namespace AzKotle.Application.PublicAccess;

public sealed record PublicInspectionResponse(
    InspectionType Type,
    string TypeLabel,
    DateTime PerformedAt,
    string TenantCompanyName,
    string? TenantLogoUrl,
    string BoilerManufacturer,
    string BoilerModel,
    bool PdfAvailable);
