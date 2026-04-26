using AzKotle.Application.Abstractions;
using AzKotle.Domain.Common;
using AzKotle.Domain.Entities.Audit;
using AzKotle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AzKotle.Infrastructure.Tenants;

public sealed class TenantBrandingService
{
    public const int MaxLogoBytes = 500 * 1024;

    public static readonly IReadOnlySet<string> AllowedContentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg",
    };

    private readonly AzKotleDbContext _db;
    private readonly IFileStorage _storage;
    private readonly TimeProvider _time;

    public TenantBrandingService(AzKotleDbContext db, IFileStorage storage, TimeProvider time)
    {
        _db = db;
        _storage = storage;
        _time = time;
    }

    public async Task<UploadLogoResult> UploadLogoAsync(
        TenantId tenantId,
        UserId actorUserId,
        ReadOnlyMemory<byte> content,
        string contentType,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct)
    {
        if (content.Length == 0)
        {
            return new UploadLogoResult.Invalid("Soubor je prázdný.");
        }
        if (content.Length > MaxLogoBytes)
        {
            return new UploadLogoResult.Invalid(
                $"Soubor je příliš velký ({content.Length / 1024} KB), maximum je {MaxLogoBytes / 1024} KB.");
        }
        if (!AllowedContentTypes.Contains(contentType))
        {
            return new UploadLogoResult.Invalid(
                $"Nepodporovaný formát {contentType}. Povolené: PNG, JPEG.");
        }
        if (!IsKnownImageMagic(content.Span, contentType))
        {
            return new UploadLogoResult.Invalid("Obsah souboru neodpovídá deklarovanému formátu.");
        }

        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null)
        {
            return new UploadLogoResult.NotFound();
        }

        var key = BuildKey(tenantId);
        await _storage.PutAsync(key, content, contentType, ct);

        tenant.SetLogo(key, _time);

        var auditLog = AuditLog.Record(
            tenantId,
            actorUserId,
            "tenant.branding_updated",
            "tenant",
            tenantId.Value,
            ipAddress,
            userAgent,
            metadataJson: $$"""{"logo_storage_key":"{{key}}","content_type":"{{contentType}}","size_bytes":{{content.Length}}}""",
            _time);
        _db.AuditLog.Add(auditLog);

        await _db.SaveChangesAsync(ct);

        return new UploadLogoResult.Success(key, tenant.LogoUpdatedAt!.Value);
    }

    public static string BuildKey(TenantId tenantId) =>
        $"tenants/{tenantId.Value:D}/branding/logo";

    private static bool IsKnownImageMagic(ReadOnlySpan<byte> bytes, string contentType)
    {
        if (string.Equals(contentType, "image/png", StringComparison.OrdinalIgnoreCase))
        {
            // PNG: 89 50 4E 47 0D 0A 1A 0A
            return bytes.Length >= 8
                && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
                && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A;
        }
        if (string.Equals(contentType, "image/jpeg", StringComparison.OrdinalIgnoreCase))
        {
            // JPEG SOI: FF D8 FF
            return bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
        }
        return false;
    }
}

public abstract record UploadLogoResult
{
    public sealed record Success(string StorageKey, DateTime LogoUpdatedAt) : UploadLogoResult;
    public sealed record Invalid(string Reason) : UploadLogoResult;
    public sealed record NotFound : UploadLogoResult;
}
