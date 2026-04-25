namespace AzKotle.Web.Client.Pages;

public sealed class LocationFormData
{
    public Guid? CustomerId { get; set; }
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Zip { get; set; } = string.Empty;
    public decimal? GpsLatitude { get; set; }
    public decimal? GpsLongitude { get; set; }
    public string? Notes { get; set; }
}
