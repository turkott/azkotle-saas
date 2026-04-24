namespace AzKotle.Core.Entities;

public class ServisniZprava : BaseEntity
{
    public Guid KotelId { get; set; }
    public Kotel Kotel { get; set; } = null!;

    public DateTime DatumZasahu { get; set; }
    public required string Technik { get; set; }
    public required string PopisUkonu { get; set; }
    public string? Zavady { get; set; }
    public string? Doporuceni { get; set; }
    public DateTime? DatumDalsihoServisu { get; set; }
}
