using AzKotle.Domain.Entities.Boilers;

namespace AzKotle.Application.Boilers;

public sealed record CreateBoilerRequest(
    Guid LocationId,
    string Manufacturer,
    string Model,
    string SerialNo,
    decimal OutputKw,
    FuelType FuelType,
    DateOnly InstalledAt,
    string? Notes = null);

public sealed record UpdateBoilerRequest(
    string Manufacturer,
    string Model,
    string SerialNo,
    decimal OutputKw,
    FuelType FuelType,
    string? Notes = null);

public sealed record RecordInspectionRequest(DateOnly PerformedAt, DateOnly? NextDueAt);

public sealed record BoilerDto(
    Guid Id,
    Guid LocationId,
    string QrCode,
    string Manufacturer,
    string Model,
    string SerialNo,
    decimal OutputKw,
    FuelType FuelType,
    DateOnly InstalledAt,
    DateOnly? LastInspectionAt,
    DateOnly? NextInspectionDue,
    string? Notes,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
