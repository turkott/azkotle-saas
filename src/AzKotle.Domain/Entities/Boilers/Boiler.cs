using System.Text.RegularExpressions;
using AzKotle.Domain.Common;

namespace AzKotle.Domain.Entities.Boilers;

public sealed partial class Boiler : DomainEntity
{
    public const int QrCodeMaxLength = 16;
    public const int ManufacturerMaxLength = 128;
    public const int ModelMaxLength = 128;
    public const int SerialNoMaxLength = 64;

    public BoilerId Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public LocationId LocationId { get; private set; }
    public string QrCode { get; private set; } = string.Empty;
    public string Manufacturer { get; private set; } = string.Empty;
    public string Model { get; private set; } = string.Empty;
    public string SerialNo { get; private set; } = string.Empty;
    public decimal OutputKw { get; private set; }
    public FuelType FuelType { get; private set; }
    public DateOnly InstalledAt { get; private set; }
    public DateOnly? LastInspectionAt { get; private set; }
    public DateOnly? NextInspectionDue { get; private set; }
    public string? Notes { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private Boiler()
    {
        // EF Core
    }

    public static Boiler Register(
        TenantId tenantId,
        LocationId locationId,
        string qrCode,
        string manufacturer,
        string model,
        string serialNo,
        decimal outputKw,
        FuelType fuelType,
        DateOnly installedAt,
        TimeProvider? timeProvider = null)
    {
        if (tenantId == TenantId.Empty)
        {
            throw new ArgumentException("Tenant musí být vyplněn.", nameof(tenantId));
        }

        if (locationId == LocationId.Empty)
        {
            throw new ArgumentException("Lokalita musí být vyplněna.", nameof(locationId));
        }

        ArgumentNullException.ThrowIfNull(qrCode);
        if (string.IsNullOrWhiteSpace(qrCode))
        {
            throw new ArgumentException("QR kód nesmí být prázdný.", nameof(qrCode));
        }
        if (!QrCodeRegex().IsMatch(qrCode))
        {
            throw new ArgumentException("QR kód musí být ve formátu AK-XXXX-XX (Crockford Base32).", nameof(qrCode));
        }

        ArgumentNullException.ThrowIfNull(manufacturer);
        if (string.IsNullOrWhiteSpace(manufacturer))
        {
            throw new ArgumentException("Výrobce nesmí být prázdný.", nameof(manufacturer));
        }
        if (manufacturer.Length > ManufacturerMaxLength)
        {
            throw new ArgumentException($"Výrobce může mít max {ManufacturerMaxLength} znaků.", nameof(manufacturer));
        }

        ArgumentNullException.ThrowIfNull(model);
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model nesmí být prázdný.", nameof(model));
        }
        if (model.Length > ModelMaxLength)
        {
            throw new ArgumentException($"Model může mít max {ModelMaxLength} znaků.", nameof(model));
        }

        ArgumentNullException.ThrowIfNull(serialNo);
        if (string.IsNullOrWhiteSpace(serialNo))
        {
            throw new ArgumentException("Sériové číslo nesmí být prázdné.", nameof(serialNo));
        }
        if (serialNo.Length > SerialNoMaxLength)
        {
            throw new ArgumentException($"Sériové číslo může mít max {SerialNoMaxLength} znaků.", nameof(serialNo));
        }

        if (outputKw <= 0m || outputKw > 9999.9m)
        {
            throw new ArgumentOutOfRangeException(nameof(outputKw),
                "Výkon musí být v rozsahu (0; 9999.9] kW.");
        }

        var now = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        if (installedAt > DateOnly.FromDateTime(now))
        {
            throw new ArgumentException("Datum instalace nesmí být v budoucnosti.", nameof(installedAt));
        }

        var boiler = new Boiler
        {
            Id = BoilerId.New(),
            TenantId = tenantId,
            LocationId = locationId,
            QrCode = qrCode,
            Manufacturer = manufacturer.Trim(),
            Model = model.Trim(),
            SerialNo = serialNo.Trim(),
            OutputKw = outputKw,
            FuelType = fuelType,
            InstalledAt = installedAt,
            CreatedAt = now,
        };
        boiler.RaiseDomainEvent(new BoilerRegistered(boiler.Id, tenantId, locationId, qrCode, now));
        return boiler;
    }

    public void RecordInspection(DateOnly performedAt, DateOnly? nextDueAt, TimeProvider? timeProvider = null)
    {
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);

        if (performedAt > today)
        {
            throw new ArgumentException("Datum revize nesmí být v budoucnosti.", nameof(performedAt));
        }

        if (performedAt < InstalledAt)
        {
            throw new ArgumentException("Revize nemůže proběhnout před instalací kotle.", nameof(performedAt));
        }

        if (nextDueAt is { } next && next <= performedAt)
        {
            throw new ArgumentException("Termín další revize musí být po aktuální revizi.", nameof(nextDueAt));
        }

        LastInspectionAt = performedAt;
        NextInspectionDue = nextDueAt;
        UpdatedAt = now;
        RaiseDomainEvent(new BoilerInspectionRecorded(Id, TenantId, now, nextDueAt, now));
    }

    public void UpdateSpecs(
        string manufacturer,
        string model,
        string serialNo,
        decimal outputKw,
        FuelType fuelType,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(manufacturer);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(serialNo);

        if (string.IsNullOrWhiteSpace(manufacturer) || manufacturer.Length > ManufacturerMaxLength)
        {
            throw new ArgumentException($"Výrobce musí být vyplněn (max {ManufacturerMaxLength} znaků).", nameof(manufacturer));
        }
        if (string.IsNullOrWhiteSpace(model) || model.Length > ModelMaxLength)
        {
            throw new ArgumentException($"Model musí být vyplněn (max {ModelMaxLength} znaků).", nameof(model));
        }
        if (string.IsNullOrWhiteSpace(serialNo) || serialNo.Length > SerialNoMaxLength)
        {
            throw new ArgumentException($"Sériové číslo musí být vyplněno (max {SerialNoMaxLength} znaků).", nameof(serialNo));
        }
        if (outputKw <= 0m || outputKw > 9999.9m)
        {
            throw new ArgumentOutOfRangeException(nameof(outputKw), "Výkon musí být v rozsahu (0; 9999.9] kW.");
        }

        Manufacturer = manufacturer.Trim();
        Model = model.Trim();
        SerialNo = serialNo.Trim();
        OutputKw = outputKw;
        FuelType = fuelType;
        UpdatedAt = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
    }

    public void MoveToLocation(LocationId newLocationId, TimeProvider? timeProvider = null)
    {
        if (newLocationId == LocationId.Empty)
        {
            throw new ArgumentException("Lokalita musí být vyplněna.", nameof(newLocationId));
        }

        LocationId = newLocationId;
        UpdatedAt = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
    }

    public void SetNotes(string? notes, TimeProvider? timeProvider = null)
    {
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        UpdatedAt = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
    }

    [GeneratedRegex("^AK-[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{2}$")]
    private static partial Regex QrCodeRegex();
}
