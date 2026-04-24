namespace AzKotle.Application.Abstractions;

public interface IAresClient
{
    Task<AresCompany?> LookupByIcoAsync(string ico, CancellationToken cancellationToken = default);
}

public sealed record AresCompany(
    string Ico,
    string? Dic,
    string CompanyName,
    string? Street,
    string? City,
    string? Zip);
