using AzKotle.Application.Abstractions;
using AzKotle.Domain.Common;
using AzKotle.Domain.Entities.Audit;
using AzKotle.Domain.Entities.Inspections;
using AzKotle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AzKotle.Infrastructure.Inspections;

public sealed class InspectionPhotoService
{
    public const int MaxPhotoBytes = 8 * 1024 * 1024;
    public const int MaxFieldIdLength = 64;
    public static readonly TimeSpan PresignedThumbnailTtl = TimeSpan.FromMinutes(5);

    private static readonly IReadOnlySet<string> _allowedContentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg",
    };

    private readonly AzKotleDbContext _db;
    private readonly IFileStorage _storage;
    private readonly TimeProvider _time;

    public InspectionPhotoService(AzKotleDbContext db, IFileStorage storage, TimeProvider time)
    {
        _db = db;
        _storage = storage;
        _time = time;
    }

    public async Task<UploadPhotoResult> UploadAsync(
        InspectionId inspectionId,
        UserId actorUserId,
        string fieldId,
        ReadOnlyMemory<byte> content,
        string contentType,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct)
    {
        if (!IsValidFieldId(fieldId))
        {
            return new UploadPhotoResult.Invalid("Neplatné ID pole.");
        }
        if (content.Length == 0)
        {
            return new UploadPhotoResult.Invalid("Soubor je prázdný.");
        }
        if (content.Length > MaxPhotoBytes)
        {
            return new UploadPhotoResult.Invalid(
                $"Soubor je příliš velký ({content.Length / 1024 / 1024} MB), maximum je {MaxPhotoBytes / 1024 / 1024} MB.");
        }
        if (!_allowedContentTypes.Contains(contentType))
        {
            return new UploadPhotoResult.Invalid(
                $"Nepodporovaný formát {contentType}. Povolené: PNG, JPEG.");
        }
        if (!IsKnownImageMagic(content.Span, contentType))
        {
            return new UploadPhotoResult.Invalid("Obsah souboru neodpovídá deklarovanému formátu.");
        }

        var inspection = await _db.Inspections
            .FirstOrDefaultAsync(i => i.Id == inspectionId, ct);
        if (inspection is null)
        {
            return new UploadPhotoResult.NotFound();
        }
        if (inspection.Status != InspectionStatus.Draft)
        {
            // Po podpisu je revize uzamčená — žádné nové fotky, jinak by se hash PDF
            // a S3 obsah rozcházely (PDF byl vygenerován z konkrétního set-u photo keys).
            return new UploadPhotoResult.InvalidState(
                $"Revize není v draft stavu (aktuální: {inspection.Status}).");
        }

        var extension = contentType.Equals("image/png", StringComparison.OrdinalIgnoreCase) ? "png" : "jpg";
        var key = BuildKey(inspection.TenantId, inspectionId, fieldId, extension);
        await _storage.PutAsync(key, content, contentType, ct);

        _db.AuditLog.Add(AuditLog.Record(
            inspection.TenantId,
            actorUserId,
            action: "inspection.photo_uploaded",
            targetType: "inspection",
            targetId: inspectionId.Value,
            ipAddress,
            userAgent,
            metadataJson: $$"""{"field_id":"{{fieldId}}","storage_key":"{{key}}","content_type":"{{contentType}}","size_bytes":{{content.Length}}}""",
            _time));
        await _db.SaveChangesAsync(ct);

        return new UploadPhotoResult.Success(key);
    }

    public async Task<IssuePhotoUrlResult> IssuePhotoUrlAsync(
        InspectionId inspectionId,
        string storageKey,
        CancellationToken ct)
    {
        var inspection = await _db.Inspections.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == inspectionId, ct);
        if (inspection is null)
        {
            return new IssuePhotoUrlResult.NotFound();
        }

        // Defense-by-construction: jediné keys, které proxy zpřístupní, jsou keys
        // pod prefixem této inspection. Cross-inspection / cross-tenant access je
        // nemožný — i kdyby si technik schoval cizí key z FormDataJson, fail-closed.
        var expectedPrefix = BuildPrefix(inspection.TenantId, inspectionId);
        if (!storageKey.StartsWith(expectedPrefix, StringComparison.Ordinal))
        {
            return new IssuePhotoUrlResult.NotFound();
        }

        var url = await _storage.CreatePresignedDownloadUrlAsync(
            storageKey, PresignedThumbnailTtl, cancellationToken: ct);
        return new IssuePhotoUrlResult.Success(url);
    }

    public static string BuildKey(TenantId tenantId, InspectionId inspectionId, string fieldId, string extension) =>
        $"tenants/{tenantId.Value:D}/inspections/{inspectionId.Value:D}/photos/{fieldId}.{extension}";

    public static string BuildPrefix(TenantId tenantId, InspectionId inspectionId) =>
        $"tenants/{tenantId.Value:D}/inspections/{inspectionId.Value:D}/photos/";

    private static bool IsValidFieldId(string fieldId)
    {
        if (string.IsNullOrWhiteSpace(fieldId) || fieldId.Length > MaxFieldIdLength)
        {
            return false;
        }
        // Field ID je schema identifier — alfanumerika + underscore. Restriktivní
        // whitelist předchází path traversal („../") při skládání S3 klíče.
        foreach (var c in fieldId)
        {
            if (c is not ((>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_' or '-'))
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsKnownImageMagic(ReadOnlySpan<byte> bytes, string contentType)
    {
        if (string.Equals(contentType, "image/png", StringComparison.OrdinalIgnoreCase))
        {
            return bytes.Length >= 8
                && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
                && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A;
        }
        if (string.Equals(contentType, "image/jpeg", StringComparison.OrdinalIgnoreCase))
        {
            return bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
        }
        return false;
    }
}

public abstract record UploadPhotoResult
{
    public sealed record Success(string StorageKey) : UploadPhotoResult;
    public sealed record Invalid(string Reason) : UploadPhotoResult;
    public sealed record InvalidState(string Reason) : UploadPhotoResult;
    public sealed record NotFound : UploadPhotoResult;
}

public abstract record IssuePhotoUrlResult
{
    public sealed record Success(string Url) : IssuePhotoUrlResult;
    public sealed record NotFound : IssuePhotoUrlResult;
}
