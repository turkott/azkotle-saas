using System.Data;
using System.Security.Cryptography;
using AzKotle.Application.Abstractions;
using AzKotle.Domain.Common;
using AzKotle.Domain.Entities.Audit;
using AzKotle.Domain.Entities.Inspections;
using AzKotle.Infrastructure.Pdf;
using AzKotle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AzKotle.Infrastructure.Inspections;

public sealed class InspectionSignService
{
    private readonly AzKotleDbContext _db;
    private readonly InspectionReportBuilder _pdfBuilder;
    private readonly IFileStorage _storage;
    private readonly TimeProvider _time;

    public InspectionSignService(
        AzKotleDbContext db,
        InspectionReportBuilder pdfBuilder,
        IFileStorage storage,
        TimeProvider time)
    {
        _db = db;
        _pdfBuilder = pdfBuilder;
        _storage = storage;
        _time = time;
    }

    public async Task<SignInspectionResult> SignAsync(
        InspectionId inspectionId,
        UserId actorUserId,
        uint expectedVersion,
        string? ipAddress,
        string? userAgent,
        byte[]? signatureData,
        CancellationToken ct)
    {
        // The whole sign flow runs inside a single DB transaction so the row lock
        // taken by SELECT … FOR UPDATE serializes concurrent sign attempts. The
        // second caller blocks until the first commits/rolls back, then sees the
        // bumped xmin and returns 409 BEFORE writing anything to S3.
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        // Postgres SELECT * omits system columns by default — explicitly list xmin
        // so EF can materialize the Version concurrency token from the locked row.
        var inspection = await _db.Inspections
            .FromSqlRaw("""SELECT *, xmin FROM "inspections" WHERE id = {0} FOR UPDATE""", inspectionId.Value)
            .FirstOrDefaultAsync(ct);
        if (inspection is null)
        {
            return new SignInspectionResult.NotFound();
        }
        if (inspection.Version != expectedVersion)
        {
            return new SignInspectionResult.StaleVersion(inspection.Version);
        }
        if (inspection.Status != InspectionStatus.Draft)
        {
            return new SignInspectionResult.InvalidState(
                $"Revize není v draft stavu (aktuální: {inspection.Status}).");
        }

        var pdf = await _pdfBuilder.RenderAsync(inspectionId, ct);
        if (pdf is null)
        {
            return new SignInspectionResult.InvalidState(
                "Nepodařilo se sestavit PDF (chybí kotel/zákazník/lokalita?).");
        }

        var sha256 = Convert.ToHexString(SHA256.HashData(pdf)).ToLowerInvariant();
        var key = BuildKey(inspection.TenantId, inspectionId, inspection.PerformedAt);

        // S3 upload happens INSIDE the transaction (after row lock + version check).
        // No concurrent sign for the same inspection can interleave a competing
        // PUT against the same key here, so DB-recorded SHA256 always matches S3.
        await _storage.PutAsync(key, pdf, "application/pdf", ct);

        try
        {
            inspection.Sign(key, sha256, signatureData, _time);
        }
        catch (InvalidOperationException ex)
        {
            return new SignInspectionResult.InvalidState(ex.Message);
        }

        var auditLog = AuditLog.Record(
            inspection.TenantId,
            actorUserId,
            "inspection.signed",
            "inspection",
            inspectionId.Value,
            ipAddress,
            userAgent,
            metadataJson: $$"""{"pdf_sha256":"{{sha256}}","pdf_b2_key":"{{key}}"}""",
            _time);
        _db.AuditLog.Add(auditLog);

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new SignInspectionResult.Success(inspection, sha256);
    }

    public static string BuildKey(TenantId tenantId, InspectionId inspectionId, DateTime performedAt) =>
        $"tenants/{tenantId.Value:D}/inspections/{performedAt:yyyy}/{inspectionId.Value:D}.pdf";
}

public abstract record SignInspectionResult
{
    public sealed record Success(Inspection Inspection, string PdfSha256) : SignInspectionResult;
    public sealed record NotFound : SignInspectionResult;
    public sealed record InvalidState(string Reason) : SignInspectionResult;
    public sealed record StaleVersion(uint CurrentVersion) : SignInspectionResult;
}
