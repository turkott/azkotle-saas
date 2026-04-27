using System.Data;
using AzKotle.Application.Abstractions;
using AzKotle.Application.PublicAccess;
using AzKotle.Domain.Common;
using AzKotle.Domain.Entities.Audit;
using AzKotle.Domain.Entities.Inspections;
using AzKotle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace AzKotle.Infrastructure.PublicAccess;

public sealed class PublicInspectionService
{
    public static readonly TimeSpan PreSignedPdfTtl = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan PreSignedLogoTtl = TimeSpan.FromMinutes(5);

    private readonly AzKotleDbContext _db;
    private readonly IFileStorage _storage;
    private readonly TimeProvider _time;

    public PublicInspectionService(AzKotleDbContext db, IFileStorage storage, TimeProvider time)
    {
        _db = db;
        _storage = storage;
        _time = time;
    }

    public async Task<PublicInspectionLookupResult> GetSummaryAsync(
        string accessHash,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct)
    {
        // ReadCommitted is sufficient: we just need the same physical connection
        // across the SECURITY DEFINER call → set_config → audit insert so the
        // tenant context written between commands stays effective.
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        var row = await LookupAsync(accessHash, ct);
        if (row is null)
        {
            return new PublicInspectionLookupResult.NotFound();
        }

        await SetTenantContextAsync(row.TenantId, ct);

        string? logoUrl = null;
        if (!string.IsNullOrWhiteSpace(row.TenantLogoStorageKey))
        {
            try
            {
                logoUrl = await _storage.CreatePresignedDownloadUrlAsync(
                    row.TenantLogoStorageKey,
                    PreSignedLogoTtl,
                    cancellationToken: ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Logo musí být nice-to-have; pokud S3 selže, page se vyrenderuje
                // bez loga než aby selhal celý view (UX > vizuál).
                logoUrl = null;
            }
        }

        _db.AuditLog.Add(AuditLog.Record(
            new TenantId(row.TenantId),
            actorUserId: null,
            action: "inspection.public_viewed",
            targetType: "inspection",
            targetId: row.InspectionId,
            ipAddress,
            userAgent,
            metadataJson: $$"""{"access_hash":"{{row.AccessHashRedactedHint}}"}""",
            _time));

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var response = new PublicInspectionResponse(
            Type: row.Type,
            TypeLabel: TypeLabel(row.Type),
            PerformedAt: row.PerformedAt,
            TenantCompanyName: row.TenantCompanyName,
            TenantLogoUrl: logoUrl,
            BoilerManufacturer: row.BoilerManufacturer,
            BoilerModel: row.BoilerModel,
            PdfAvailable: !string.IsNullOrWhiteSpace(row.PdfB2Key));

        return new PublicInspectionLookupResult.Success(response);
    }

    public async Task<PublicInspectionPdfResult> IssuePdfUrlAsync(
        string accessHash,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        var row = await LookupAsync(accessHash, ct);
        if (row is null || string.IsNullOrWhiteSpace(row.PdfB2Key))
        {
            return new PublicInspectionPdfResult.NotFound();
        }

        await SetTenantContextAsync(row.TenantId, ct);

        var fileName = $"protokol-{ShortNumber(row.InspectionId, row.PerformedAt)}.pdf";
        var url = await _storage.CreatePresignedDownloadUrlAsync(
            row.PdfB2Key,
            PreSignedPdfTtl,
            downloadFileName: fileName,
            cancellationToken: ct);

        var ttlSeconds = (int)PreSignedPdfTtl.TotalSeconds;
        _db.AuditLog.Add(AuditLog.Record(
            new TenantId(row.TenantId),
            actorUserId: null,
            action: "inspection.public_pdf_downloaded",
            targetType: "inspection",
            targetId: row.InspectionId,
            ipAddress,
            userAgent,
            metadataJson: $$"""{"ttl_seconds":{{ttlSeconds}},"pdf_b2_key":"{{row.PdfB2Key}}"}""",
            _time));

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new PublicInspectionPdfResult.Success(url);
    }

    private async Task<LookupRow?> LookupAsync(string accessHash, CancellationToken ct)
    {
        var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
        {
            await conn.OpenAsync(ct);
        }

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = (NpgsqlTransaction?)_db.Database.CurrentTransaction?.GetDbTransaction();
        cmd.CommandText = "SELECT * FROM public.find_public_inspection(@hash)";
        cmd.Parameters.Add(new NpgsqlParameter("hash", NpgsqlDbType.Text) { Value = accessHash });

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        var typeText = reader.GetString(3);
        if (!Enum.TryParse<InspectionType>(typeText, ignoreCase: false, out var type))
        {
            type = InspectionType.AnnualNv191;
        }

        return new LookupRow(
            InspectionId: reader.GetGuid(0),
            TenantId: reader.GetGuid(1),
            PerformedAt: DateTime.SpecifyKind(reader.GetDateTime(2), DateTimeKind.Utc),
            Type: type,
            PdfB2Key: reader.IsDBNull(4) ? null : reader.GetString(4),
            TenantCompanyName: reader.GetString(5),
            TenantLogoStorageKey: reader.IsDBNull(6) ? null : reader.GetString(6),
            BoilerManufacturer: reader.GetString(7),
            BoilerModel: reader.GetString(8),
            // Audit log uchovává jen prefix — celý hash je equivalent k credentialu;
            // logování celého hashe by ho udělalo dohledatelným v audit datech a
            // efektivně by ho prozradilo komukoli s přístupem k logům.
            AccessHashRedactedHint: accessHash.Length > 8 ? accessHash[..8] + "…" : "***");
    }

    private async Task SetTenantContextAsync(Guid tenantId, CancellationToken ct)
    {
        await _db.Database.ExecuteSqlRawAsync(
            "SELECT set_config('app.current_tenant_id', {0}, false)",
            new object[] { tenantId.ToString() },
            ct);
    }

    private static string ShortNumber(Guid inspectionId, DateTime performedAt) =>
        $"{performedAt:yyyy}-{inspectionId.ToString("N")[..8].ToUpperInvariant()}";

    private static string TypeLabel(InspectionType type) => type switch
    {
        InspectionType.AnnualNv191 => "Roční prohlídka spotřebiče",
        InspectionType.Tpg704_01Service => "Servis plynového zařízení",
        InspectionType.Emergency => "Mimořádná kontrola",
        _ => type.ToString(),
    };

    private sealed record LookupRow(
        Guid InspectionId,
        Guid TenantId,
        DateTime PerformedAt,
        InspectionType Type,
        string? PdfB2Key,
        string TenantCompanyName,
        string? TenantLogoStorageKey,
        string BoilerManufacturer,
        string BoilerModel,
        string AccessHashRedactedHint);
}

public abstract record PublicInspectionLookupResult
{
    public sealed record Success(PublicInspectionResponse Response) : PublicInspectionLookupResult;
    public sealed record NotFound : PublicInspectionLookupResult;
}

public abstract record PublicInspectionPdfResult
{
    public sealed record Success(string Url) : PublicInspectionPdfResult;
    public sealed record NotFound : PublicInspectionPdfResult;
}
