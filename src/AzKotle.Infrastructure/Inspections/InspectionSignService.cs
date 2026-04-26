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
        string? ipAddress,
        string? userAgent,
        byte[]? signatureData,
        CancellationToken ct)
    {
        var inspection = await _db.Inspections.FirstOrDefaultAsync(i => i.Id == inspectionId, ct);
        if (inspection is null)
        {
            return new SignInspectionResult.NotFound();
        }
        if (inspection.Status != InspectionStatus.Draft)
        {
            return new SignInspectionResult.InvalidState(
                $"Revize není v draft stavu (aktuální: {inspection.Status}).");
        }

        var pdf = await _pdfBuilder.RenderAsync(inspectionId, ct);
        if (pdf is null)
        {
            return new SignInspectionResult.InvalidState("Nepodařilo se sestavit PDF (chybí kotel/zákazník/lokalita?).");
        }

        var sha256 = Convert.ToHexString(SHA256.HashData(pdf)).ToLowerInvariant();
        var key = BuildKey(inspection.TenantId, inspectionId, inspection.PerformedAt);

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
}
