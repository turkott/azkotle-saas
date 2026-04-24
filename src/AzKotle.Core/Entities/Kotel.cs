namespace AzKotle.Core.Entities;

public class Kotel : BaseEntity
{
    public required string Vyrobce { get; set; }
    public required string Model { get; set; }
    public required string VyrobniCislo { get; set; }
    public int? RokVyroby { get; set; }
    public decimal? VykonKw { get; set; }
    public Palivo Palivo { get; set; }

    public required string VlastnikJmeno { get; set; }
    public string? VlastnikTelefon { get; set; }
    public string? VlastnikEmail { get; set; }
    public required string Umisteni { get; set; }

    public List<ServisniZprava> ServisniZpravy { get; set; } = [];
}
