using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using AzKotle.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace AzKotle.Infrastructure.Storage;

public sealed class S3FileStorage : IFileStorage, IDisposable
{
    private static readonly ResiliencePipeline _retryPipeline = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            Delay = TimeSpan.FromMilliseconds(200),
            UseJitter = true,
            ShouldHandle = new PredicateBuilder().Handle<AmazonS3Exception>(static ex =>
                (int)ex.StatusCode is 0 or 408 or 429 or >= 500),
        })
        .Build();

    private readonly StorageOptions _options;
    private readonly ILogger<S3FileStorage> _logger;
    private readonly AmazonS3Client _client;

    public S3FileStorage(IOptions<StorageOptions> options, ILogger<S3FileStorage> logger)
    {
        _options = options.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_options.AccessKey) || string.IsNullOrWhiteSpace(_options.SecretKey))
        {
            throw new InvalidOperationException("Storage:AccessKey/SecretKey nejsou nastavené.");
        }

        var credentials = new BasicAWSCredentials(_options.AccessKey, _options.SecretKey);
        var config = new AmazonS3Config
        {
            ServiceURL = _options.ServiceUrl,
            ForcePathStyle = _options.ForcePathStyle,
            UseHttp = _options.ServiceUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase),
            AuthenticationRegion = _options.Region,
        };
        _client = new AmazonS3Client(credentials, config);
    }

    public async Task PutAsync(string key, ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        await _retryPipeline.ExecuteAsync(async ct =>
        {
            using var stream = new MemoryStream(content.ToArray(), writable: false);
            var request = new PutObjectRequest
            {
                BucketName = _options.Bucket,
                Key = key,
                InputStream = stream,
                ContentType = contentType,
            };
            await _client.PutObjectAsync(request, ct);
        }, cancellationToken);
    }

    public async Task<Stream?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        try
        {
            var response = await _retryPipeline.ExecuteAsync(async ct =>
                await _client.GetObjectAsync(_options.Bucket, key, ct), cancellationToken);

            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex) when ((int)ex.StatusCode == 404)
        {
            return null;
        }
    }

    public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        try
        {
            await _retryPipeline.ExecuteAsync(async ct =>
                await _client.DeleteObjectAsync(_options.Bucket, key, ct), cancellationToken);
            return true;
        }
        catch (AmazonS3Exception ex) when ((int)ex.StatusCode == 404)
        {
            return false;
        }
    }

    public Task<string> CreatePresignedDownloadUrlAsync(
        string key,
        TimeSpan ttl,
        string? downloadFileName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (ttl <= TimeSpan.Zero || ttl > TimeSpan.FromDays(7))
        {
            throw new ArgumentOutOfRangeException(nameof(ttl), "TTL musí být v intervalu (0; 7] dní.");
        }

        var request = new GetPreSignedUrlRequest
        {
            BucketName = _options.Bucket,
            Key = key,
            Expires = DateTime.UtcNow.Add(ttl),
            Verb = HttpVerb.GET,
        };
        if (!string.IsNullOrWhiteSpace(downloadFileName))
        {
            // RFC 6266: ASCII-safe filename in quotes; for unicode names, filename* would be needed.
            var safeName = SanitizeFileName(downloadFileName);
            request.ResponseHeaderOverrides.ContentDisposition = $"attachment; filename=\"{safeName}\"";
        }
        return _client.GetPreSignedURLAsync(request);
    }

    private static string SanitizeFileName(string fileName)
    {
        Span<char> buffer = stackalloc char[fileName.Length];
        var written = 0;
        foreach (var c in fileName)
        {
            buffer[written++] = c switch
            {
                '"' or '\\' or '\r' or '\n' => '_',
                _ => c,
            };
        }
        return new string(buffer[..written]);
    }

    public async Task<bool> HeadBucketAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Backblaze B2 application keys scoped to readFiles/writeFiles return
            // 403 on HeadBucket (it maps to b2_list_buckets needing listBuckets
            // capability). HEAD on a sentinel object is universally supported
            // and tells us the same thing: 404 = reachable + auth OK.
            await _client.GetObjectMetadataAsync(new GetObjectMetadataRequest
            {
                BucketName = _options.Bucket,
                Key = "__probe__/healthcheck",
            }, cancellationToken);
            return true;
        }
        catch (AmazonS3Exception ex) when ((int)ex.StatusCode == 404)
        {
            // Successfully reached bucket and authenticated — sentinel doesn't
            // exist (normal state). This IS the success signal we want.
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "S3 readiness probe failed for bucket {Bucket}", _options.Bucket);
            return false;
        }
    }

    public async Task EnsureBucketExistsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.PutBucketAsync(new PutBucketRequest
            {
                BucketName = _options.Bucket,
            }, cancellationToken);
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode is "BucketAlreadyOwnedByYou" or "BucketAlreadyExists")
        {
            // ok
        }
    }

    public void Dispose() => _client.Dispose();
}
