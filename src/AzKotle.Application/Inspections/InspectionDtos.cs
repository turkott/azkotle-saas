using AzKotle.Domain.Entities.Inspections;

namespace AzKotle.Application.Inspections;

public sealed record CreateInspectionRequest(
    Guid BoilerId,
    InspectionType Type,
    DateTime PerformedAt);

public sealed record UpdateInspectionDraftRequest(
    string FormDataJson,
    string? Findings,
    string? Recommendations,
    DateOnly? NextDueAt,
    uint Version);

public sealed record SignInspectionRequest(string? SignatureBase64, uint Version);

public sealed record SignedInspectionResponse(
    InspectionDto Inspection,
    string PdfSha256);

public sealed record InspectionDto(
    Guid Id,
    Guid BoilerId,
    Guid TechnicianId,
    InspectionType Type,
    DateTime PerformedAt,
    InspectionStatus Status,
    string FormDataJson,
    string? Findings,
    string? Recommendations,
    DateOnly? NextDueAt,
    string? PdfB2Key,
    string? PdfSha256,
    DateTime? SignedAt,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    uint Version);
