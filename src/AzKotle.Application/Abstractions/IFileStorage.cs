namespace AzKotle.Application.Abstractions;

public interface IFileStorage
{
    Task PutAsync(string key, ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken = default);

    Task<Stream?> GetAsync(string key, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default);

    Task<string> CreatePresignedDownloadUrlAsync(
        string key,
        TimeSpan ttl,
        string? downloadFileName = null,
        CancellationToken cancellationToken = default);
}

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string Bucket { get; set; } = "azkotle";
    public string ServiceUrl { get; set; } = "https://s3.eu-central-003.backblazeb2.com";
    public string Region { get; set; } = "eu-central-003";
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public bool ForcePathStyle { get; set; } = true;
}
