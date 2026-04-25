using AzKotle.Domain.Common;

namespace AzKotle.Domain.Entities.Inspections;

public sealed record InspectionDrafted(
    InspectionId InspectionId,
    TenantId TenantId,
    BoilerId BoilerId,
    UserId TechnicianId,
    InspectionType Type,
    DateTime OccurredAt) : IDomainEvent;

public sealed record InspectionSigned(
    InspectionId InspectionId,
    TenantId TenantId,
    BoilerId BoilerId,
    string PdfB2Key,
    string PdfSha256,
    DateTime OccurredAt) : IDomainEvent;
