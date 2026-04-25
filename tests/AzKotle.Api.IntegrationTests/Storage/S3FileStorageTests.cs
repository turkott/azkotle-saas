using System.Net.Http;
using System.Text;
using AzKotle.Application.Abstractions;
using AzKotle.Infrastructure.Storage;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.Minio;

namespace AzKotle.Api.IntegrationTests.Storage;

public sealed class S3FileStorageTests : IAsyncLifetime
{
    private readonly MinioContainer _minio = new MinioBuilder()
        .WithImage("minio/minio:RELEASE.2024-12-13T22-19-12Z")
        .WithUsername("minioadmin")
        .WithPassword("minioadmin")
        .Build();

    private S3FileStorage _storage = default!;

    public async Task InitializeAsync()
    {
        await _minio.StartAsync();

        var options = Options.Create(new StorageOptions
        {
            Bucket = "azkotle-test",
            ServiceUrl = $"http://{_minio.Hostname}:{_minio.GetMappedPublicPort(9000)}",
            Region = "us-east-1",
            AccessKey = "minioadmin",
            SecretKey = "minioadmin",
            ForcePathStyle = true,
        });

        _storage = new S3FileStorage(options, NullLogger<S3FileStorage>.Instance);
        await _storage.EnsureBucketExistsAsync();
    }

    public async Task DisposeAsync()
    {
        _storage.Dispose();
        await _minio.DisposeAsync();
    }

    [Fact]
    public async Task Put_Then_Get_RoundTripsBytes()
    {
        var payload = "Hello AZ KOTLE"u8.ToArray();
        await _storage.PutAsync("tenants/abc/file.txt", payload, "text/plain");

        await using var stream = await _storage.GetAsync("tenants/abc/file.txt");
        stream.Should().NotBeNull();
        using var reader = new StreamReader(stream!, Encoding.UTF8);
        var read = await reader.ReadToEndAsync();
        read.Should().Be("Hello AZ KOTLE");
    }

    [Fact]
    public async Task Get_NonExistent_ReturnsNull()
    {
        var stream = await _storage.GetAsync("tenants/abc/missing.bin");
        stream.Should().BeNull();
    }

    [Fact]
    public async Task Delete_RemovesObject()
    {
        await _storage.PutAsync("tenants/abc/del.txt", new byte[] { 1, 2, 3 }, "application/octet-stream");
        var deleted = await _storage.DeleteAsync("tenants/abc/del.txt");
        deleted.Should().BeTrue();

        var after = await _storage.GetAsync("tenants/abc/del.txt");
        after.Should().BeNull();
    }

    [Fact]
    public async Task PresignedUrl_ContainsKeyAndSignature()
    {
        var payload = "presigned"u8.ToArray();
        await _storage.PutAsync("tenants/abc/pre.txt", payload, "text/plain");

        var url = await _storage.CreatePresignedDownloadUrlAsync("tenants/abc/pre.txt", TimeSpan.FromMinutes(5));
        url.Should().StartWith("http");
        url.Should().Contain("pre.txt");
        url.Should().Contain("X-Amz-Signature");
    }

    [Fact]
    public async Task PresignedUrl_TooLongTtl_Throws()
    {
        var act = () => _storage.CreatePresignedDownloadUrlAsync("k", TimeSpan.FromDays(8));
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }
}
