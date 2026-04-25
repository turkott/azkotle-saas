using AzKotle.Domain.Common;

namespace AzKotle.Domain.Entities.Boilers;

public sealed record BoilerRegistered(
    BoilerId BoilerId,
    TenantId TenantId,
    LocationId LocationId,
    string QrCode,
    DateTime OccurredAt) : IDomainEvent;

public sealed record BoilerInspectionRecorded(
    BoilerId BoilerId,
    TenantId TenantId,
    DateTime PerformedAt,
    DateOnly? NextDueAt,
    DateTime OccurredAt) : IDomainEvent;
