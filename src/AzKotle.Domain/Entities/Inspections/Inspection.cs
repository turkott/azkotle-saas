using AzKotle.Domain.Common;

namespace AzKotle.Domain.Entities.Inspections;

public sealed class Inspection : DomainEntity
{
    public const int FindingsMaxLength = 4000;
    public const int RecommendationsMaxLength = 4000;
    public const int PdfB2KeyMaxLength = 512;
    public const int PdfSha256Length = 64;

    public InspectionId Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public BoilerId BoilerId { get; private set; }
    public UserId TechnicianId { get; private set; }
    public InspectionType Type { get; private set; }
    public DateTime PerformedAt { get; private set; }
    public InspectionStatus Status { get; private set; }
    public string FormDataJson { get; private set; } = "{}";
    public string? Findings { get; private set; }
    public string? Recommendations { get; private set; }
    public DateOnly? NextDueAt { get; private set; }
    public string? PdfB2Key { get; private set; }
    public string? PdfSha256 { get; private set; }
    public DateTime? SignedAt { get; private set; }
    public byte[]? SignatureData { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private Inspection()
    {
        // EF Core
    }

    public static Inspection Draft(
        TenantId tenantId,
        BoilerId boilerId,
        UserId technicianId,
        InspectionType type,
        DateTime performedAt,
        TimeProvider? timeProvider = null)
    {
        if (tenantId == TenantId.Empty)
        {
            throw new ArgumentException("Tenant musí být vyplněn.", nameof(tenantId));
        }
        if (boilerId == BoilerId.Empty)
        {
            throw new ArgumentException("Kotel musí být vyplněn.", nameof(boilerId));
        }
        if (technicianId == UserId.Empty)
        {
            throw new ArgumentException("Technik musí být vyplněn.", nameof(technicianId));
        }

        var now = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        if (performedAt > now.AddMinutes(5))
        {
            throw new ArgumentException("Datum revize nesmí být v budoucnosti.", nameof(performedAt));
        }

        var inspection = new Inspection
        {
            Id = InspectionId.New(),
            TenantId = tenantId,
            BoilerId = boilerId,
            TechnicianId = technicianId,
            Type = type,
            PerformedAt = performedAt,
            Status = InspectionStatus.Draft,
            FormDataJson = "{}",
            CreatedAt = now,
        };
        inspection.RaiseDomainEvent(new InspectionDrafted(
            inspection.Id, tenantId, boilerId, technicianId, type, now));
        return inspection;
    }

    public void UpdateFormData(string formDataJson, TimeProvider? timeProvider = null)
    {
        AssertDraft();
        ArgumentNullException.ThrowIfNull(formDataJson);
        FormDataJson = formDataJson;
        Touch(timeProvider);
    }

    public void UpdateNarrative(string? findings, string? recommendations, DateOnly? nextDueAt, TimeProvider? timeProvider = null)
    {
        AssertDraft();

        if (findings is { Length: > FindingsMaxLength })
        {
            throw new ArgumentException($"Závady mohou mít max {FindingsMaxLength} znaků.", nameof(findings));
        }
        if (recommendations is { Length: > RecommendationsMaxLength })
        {
            throw new ArgumentException($"Doporučení mohou mít max {RecommendationsMaxLength} znaků.", nameof(recommendations));
        }
        if (nextDueAt is { } due && due <= DateOnly.FromDateTime(PerformedAt))
        {
            throw new ArgumentException("Další termín musí být po datu revize.", nameof(nextDueAt));
        }

        Findings = string.IsNullOrWhiteSpace(findings) ? null : findings.Trim();
        Recommendations = string.IsNullOrWhiteSpace(recommendations) ? null : recommendations.Trim();
        NextDueAt = nextDueAt;
        Touch(timeProvider);
    }

    public void Sign(string pdfB2Key, string pdfSha256, byte[]? signatureData, TimeProvider? timeProvider = null)
    {
        AssertDraft();
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfB2Key);
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfSha256);

        if (pdfB2Key.Length > PdfB2KeyMaxLength)
        {
            throw new ArgumentException($"PDF klíč může mít max {PdfB2KeyMaxLength} znaků.", nameof(pdfB2Key));
        }
        if (pdfSha256.Length != PdfSha256Length)
        {
            throw new ArgumentException(
                $"PDF SHA-256 musí mít přesně {PdfSha256Length} hex znaků.", nameof(pdfSha256));
        }

        var now = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        Status = InspectionStatus.Signed;
        PdfB2Key = pdfB2Key;
        PdfSha256 = pdfSha256.ToLowerInvariant();
        SignatureData = signatureData;
        SignedAt = now;
        UpdatedAt = now;

        RaiseDomainEvent(new InspectionSigned(Id, TenantId, BoilerId, pdfB2Key, PdfSha256, now));
    }

    public void Archive(TimeProvider? timeProvider = null)
    {
        if (Status != InspectionStatus.Signed)
        {
            throw new InvalidOperationException("Archivovat lze jen podepsané revize.");
        }
        Status = InspectionStatus.Archived;
        Touch(timeProvider);
    }

    private void AssertDraft()
    {
        if (Status != InspectionStatus.Draft)
        {
            throw new InvalidOperationException(
                $"Revize není v draft stavu (aktuální: {Status}).");
        }
    }

    private void Touch(TimeProvider? timeProvider) =>
        UpdatedAt = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
}
