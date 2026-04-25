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
    DateOnly? NextDueAt);

public sealed record SignInspectionRequest(string? SignatureBase64);

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
    DateTime? UpdatedAt);
