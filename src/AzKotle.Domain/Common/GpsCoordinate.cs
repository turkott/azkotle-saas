namespace AzKotle.Domain.Common;

public readonly record struct GpsCoordinate
{
    public decimal Latitude { get; }
    public decimal Longitude { get; }

    public GpsCoordinate(decimal latitude, decimal longitude)
    {
        if (latitude is < -90m or > 90m)
        {
            throw new ArgumentOutOfRangeException(nameof(latitude), "Zeměpisná šířka musí být v rozsahu -90 až 90.");
        }

        if (longitude is < -180m or > 180m)
        {
            throw new ArgumentOutOfRangeException(nameof(longitude), "Zeměpisná délka musí být v rozsahu -180 až 180.");
        }

        Latitude = latitude;
        Longitude = longitude;
    }
}
