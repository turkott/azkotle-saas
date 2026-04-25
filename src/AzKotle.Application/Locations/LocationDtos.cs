namespace AzKotle.Application.Locations;

public sealed record CreateLocationRequest(
    Guid CustomerId,
    string Street,
    string City,
    string Zip,
    decimal? GpsLatitude = null,
    decimal? GpsLongitude = null,
    string? Notes = null);

public sealed record UpdateLocationRequest(
    string Street,
    string City,
    string Zip,
    decimal? GpsLatitude = null,
    decimal? GpsLongitude = null,
    string? Notes = null);

public sealed record LocationDto(
    Guid Id,
    Guid CustomerId,
    string Street,
    string City,
    string Zip,
    decimal? GpsLatitude,
    decimal? GpsLongitude,
    string? Notes,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
