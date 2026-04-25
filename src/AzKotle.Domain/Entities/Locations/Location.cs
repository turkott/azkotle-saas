using AzKotle.Domain.Common;

namespace AzKotle.Domain.Entities.Locations;

public sealed class Location : DomainEntity
{
    public const int StreetMaxLength = 255;
    public const int CityMaxLength = 128;
    public const int ZipMaxLength = 16;

    public LocationId Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public CustomerId CustomerId { get; private set; }
    public string Street { get; private set; } = string.Empty;
    public string City { get; private set; } = string.Empty;
    public string Zip { get; private set; } = string.Empty;
    public decimal? GpsLatitude { get; private set; }
    public decimal? GpsLongitude { get; private set; }
    public string? Notes { get; private set; }

    public GpsCoordinate? Gps => GpsLatitude.HasValue && GpsLongitude.HasValue
        ? new GpsCoordinate(GpsLatitude.Value, GpsLongitude.Value)
        : null;
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private Location()
    {
        // EF Core
    }

    public static Location Create(
        TenantId tenantId,
        CustomerId customerId,
        string street,
        string city,
        string zip,
        TimeProvider? timeProvider = null)
    {
        if (tenantId == TenantId.Empty)
        {
            throw new ArgumentException("Tenant musí být vyplněn.", nameof(tenantId));
        }

        if (customerId == CustomerId.Empty)
        {
            throw new ArgumentException("Zákazník musí být vyplněn.", nameof(customerId));
        }

        ValidateAddress(street, city, zip);

        var now = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        var location = new Location
        {
            Id = LocationId.New(),
            TenantId = tenantId,
            CustomerId = customerId,
            Street = street.Trim(),
            City = city.Trim(),
            Zip = zip.Trim(),
            CreatedAt = now,
        };
        location.RaiseDomainEvent(new LocationCreated(location.Id, tenantId, customerId, now));
        return location;
    }

    public void UpdateAddress(string street, string city, string zip, TimeProvider? timeProvider = null)
    {
        ValidateAddress(street, city, zip);
        Street = street.Trim();
        City = city.Trim();
        Zip = zip.Trim();
        Touch(timeProvider);
    }

    public void SetGps(decimal latitude, decimal longitude, TimeProvider? timeProvider = null)
    {
        var coord = new GpsCoordinate(latitude, longitude);
        GpsLatitude = coord.Latitude;
        GpsLongitude = coord.Longitude;
        Touch(timeProvider);
    }

    public void ClearGps(TimeProvider? timeProvider = null)
    {
        GpsLatitude = null;
        GpsLongitude = null;
        Touch(timeProvider);
    }

    public void SetNotes(string? notes, TimeProvider? timeProvider = null)
    {
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        Touch(timeProvider);
    }

    private void Touch(TimeProvider? timeProvider) =>
        UpdatedAt = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    private static void ValidateAddress(string street, string city, string zip)
    {
        ArgumentNullException.ThrowIfNull(street);
        ArgumentNullException.ThrowIfNull(city);
        ArgumentNullException.ThrowIfNull(zip);

        if (string.IsNullOrWhiteSpace(street))
        {
            throw new ArgumentException("Ulice nesmí být prázdná.", nameof(street));
        }
        if (street.Length > StreetMaxLength)
        {
            throw new ArgumentException($"Ulice může mít max {StreetMaxLength} znaků.", nameof(street));
        }
        if (string.IsNullOrWhiteSpace(city))
        {
            throw new ArgumentException("Město nesmí být prázdné.", nameof(city));
        }
        if (city.Length > CityMaxLength)
        {
            throw new ArgumentException($"Město může mít max {CityMaxLength} znaků.", nameof(city));
        }
        if (string.IsNullOrWhiteSpace(zip))
        {
            throw new ArgumentException("PSČ nesmí být prázdné.", nameof(zip));
        }
        if (zip.Length > ZipMaxLength)
        {
            throw new ArgumentException($"PSČ může mít max {ZipMaxLength} znaků.", nameof(zip));
        }
    }
}
