using AzKotle.Application.Abstractions;
using AzKotle.Domain.Common;
using AzKotle.Domain.Entities.Audit;
using AzKotle.Domain.Entities.Inspections;
using AzKotle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AzKotle.Infrastructure.Inspections;

public sealed class InspectionPdfDownloadService
{
    public static readonly TimeSpan PreSignedUrlTtl = TimeSpan.FromMinutes(5);

    private readonly AzKotleDbContext _db;
    private readonly IFileStorage _storage;
    private readonly TimeProvider _time;

    public InspectionPdfDownloadService(AzKotleDbContext db, IFileStorage storage, TimeProvider time)
    {
        _db = db;
        _storage = storage;
        _time = time;
    }

    public async Task<IssuePdfUrlResult> IssueDownloadUrlAsync(
        InspectionId inspectionId,
        UserId actorUserId,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct)
    {
        var inspection = await _db.Inspections.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == inspectionId, ct);
        if (inspection is null || string.IsNullOrWhiteSpace(inspection.PdfB2Key))
        {
            return new IssuePdfUrlResult.NotFound();
        }

        var fileName = $"protokol-{ShortNumber(inspection.Id, inspection.PerformedAt)}.pdf";
        var url = await _storage.CreatePresignedDownloadUrlAsync(
            inspection.PdfB2Key,
            PreSignedUrlTtl,
            downloadFileName: fileName,
            cancellationToken: ct);

        var ttlSeconds = (int)PreSignedUrlTtl.TotalSeconds;
        var auditLog = AuditLog.Record(
            inspection.TenantId,
            actorUserId,
            "inspection.pdf_url_issued",
            "inspection",
            inspectionId.Value,
            ipAddress,
            userAgent,
            metadataJson: $$"""{"ttl_seconds":{{ttlSeconds}},"pdf_b2_key":"{{inspection.PdfB2Key}}"}""",
            _time);
        _db.AuditLog.Add(auditLog);
        await _db.SaveChangesAsync(ct);

        return new IssuePdfUrlResult.Success(url, fileName, PreSignedUrlTtl);
    }

    private static string ShortNumber(InspectionId id, DateTime performedAt) =>
        $"{performedAt:yyyy}-{id.Value.ToString("N")[..8].ToUpperInvariant()}";
}

public abstract record IssuePdfUrlResult
{
    public sealed record Success(string Url, string FileName, TimeSpan Ttl) : IssuePdfUrlResult;
    public sealed record NotFound : IssuePdfUrlResult;
}
