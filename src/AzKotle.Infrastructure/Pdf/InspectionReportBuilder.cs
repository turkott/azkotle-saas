using System.Text.Json;
using AzKotle.Application.Abstractions;
using AzKotle.Application.Inspections;
using AzKotle.Domain.Common;
using AzKotle.Domain.Entities.Boilers;
using AzKotle.Domain.Entities.Inspections;
using AzKotle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AzKotle.Infrastructure.Pdf;

public sealed class InspectionReportBuilder
{
    private readonly AzKotleDbContext _db;
    private readonly IInspectionReportPdfRenderer _renderer;
    private readonly FormSectionMapper _formSectionMapper;
    private readonly IInspectionTemplateProvider _templates;
    private readonly IFileStorage _storage;
    private readonly ILogger<InspectionReportBuilder> _logger;

    public InspectionReportBuilder(
        AzKotleDbContext db,
        IInspectionReportPdfRenderer renderer,
        FormSectionMapper formSectionMapper,
        IInspectionTemplateProvider templates,
        IFileStorage storage,
        ILogger<InspectionReportBuilder> logger)
    {
        _db = db;
        _renderer = renderer;
        _formSectionMapper = formSectionMapper;
        _templates = templates;
        _storage = storage;
        _logger = logger;
    }

    public async Task<byte[]?> RenderAsync(InspectionId inspectionId, CancellationToken ct)
    {
        var inspection = await _db.Inspections.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == inspectionId, ct);
        if (inspection is null)
        {
            return null;
        }

        var boiler = await _db.Boilers.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == inspection.BoilerId, ct);
        if (boiler is null)
        {
            return null;
        }

        var location = await _db.Locations.AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == boiler.LocationId, ct);
        if (location is null)
        {
            return null;
        }

        var customer = await _db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == location.CustomerId, ct);
        if (customer is null)
        {
            return null;
        }

        var technician = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == inspection.TechnicianId, ct);
        if (technician is null)
        {
            return null;
        }

        var tenant = await _db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == inspection.TenantId, ct);
        if (tenant is null)
        {
            return null;
        }

        var logoImage = await TryFetchLogoAsync(tenant.LogoStorageKey, ct);
        var photos = await FetchPhotosAsync(inspection.Type, inspection.FormDataJson, ct);

        var data = new InspectionReportData(
            InspectionNumber: ShortNumber(inspection.Id, inspection.PerformedAt),
            Type: inspection.Type,
            PerformedAt: inspection.PerformedAt,
            NextDueAt: inspection.NextDueAt,
            Tenant: new InspectionReportTenant(tenant.CompanyName, tenant.Ico, tenant.Dic),
            Technician: new InspectionReportTechnician(technician.FullName, technician.TechnicianLicenseNo),
            Customer: new InspectionReportCustomer(customer.Name, customer.Ico, customer.Email, customer.Phone),
            Location: new InspectionReportLocation(location.Street, location.City, location.Zip),
            Boiler: new InspectionReportBoiler(
                boiler.QrCode, boiler.Manufacturer, boiler.Model, boiler.SerialNo,
                boiler.OutputKw, FuelLabel(boiler.FuelType), boiler.InstalledAt),
            FormSections: _formSectionMapper.Map(inspection.Type, inspection.FormDataJson),
            Findings: inspection.Findings,
            Recommendations: inspection.Recommendations,
            SignatureImage: inspection.SignatureData,
            LogoImage: logoImage,
            Photos: photos);

        return _renderer.Render(data);
    }

    private async Task<byte[]?> TryFetchLogoAsync(string? storageKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            return null;
        }
        try
        {
            await using var stream = await _storage.GetAsync(storageKey, ct);
            if (stream is null)
            {
                return null;
            }
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct);
            return ms.ToArray();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Logo failure must NEVER block the report — sign / preview still produce
            // a valid PDF without it. Operator can re-upload via tenant settings.
            _logger.LogWarning(ex, "Tenant logo fetch failed for key {LogoKey}; rendering without logo.", storageKey);
            return null;
        }
    }

    private async Task<IReadOnlyList<InspectionPhotoData>> FetchPhotosAsync(
        InspectionType type, string? formDataJson, CancellationToken ct)
    {
        var refs = CollectPhotoRefs(type, formDataJson);
        if (refs.Count == 0)
        {
            return Array.Empty<InspectionPhotoData>();
        }

        var fetches = refs.Select(r => TryFetchPhotoAsync(r.Label, r.StorageKey, ct)).ToArray();
        var results = await Task.WhenAll(fetches);

        var photos = new List<InspectionPhotoData>(results.Length);
        foreach (var photo in results)
        {
            if (photo is not null)
            {
                photos.Add(photo);
            }
        }
        return photos;
    }

    private async Task<InspectionPhotoData?> TryFetchPhotoAsync(string label, string storageKey, CancellationToken ct)
    {
        try
        {
            await using var stream = await _storage.GetAsync(storageKey, ct);
            if (stream is null)
            {
                _logger.LogWarning("Inspection photo not found in storage for key {PhotoKey} (label {Label}).", storageKey, label);
                return null;
            }
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct);
            return new InspectionPhotoData(label, ms.ToArray());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Same policy as logo: a single broken photo must not break the protocol.
            _logger.LogWarning(ex, "Inspection photo fetch failed for key {PhotoKey} (label {Label}); skipping.", storageKey, label);
            return null;
        }
    }

    private List<(string Label, string StorageKey)> CollectPhotoRefs(InspectionType type, string? formDataJson)
    {
        var refs = new List<(string Label, string StorageKey)>();
        if (string.IsNullOrWhiteSpace(formDataJson) || formDataJson == "{}")
        {
            return refs;
        }
        var template = _templates.GetTemplate(type);
        if (template is null)
        {
            return refs;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(formDataJson);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "FormDataJson could not be parsed for inspection type {Type}; skipping photo gallery.", type);
            return refs;
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return refs;
            }

            foreach (var section in template.Sections)
            {
                foreach (var field in section.Fields)
                {
                    if (!string.Equals(field.Type, "photo", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    if (!doc.RootElement.TryGetProperty(field.Id, out var value)
                        || value.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }
                    var key = value.GetString();
                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        refs.Add((field.Label, key));
                    }
                }
            }
        }

        return refs;
    }

    public static string ShortNumber(InspectionId id, DateTime performedAt) =>
        $"{performedAt:yyyy}-{id.Value.ToString("N")[..8].ToUpperInvariant()}";

    private static string FuelLabel(FuelType fuel) => fuel switch
    {
        FuelType.NaturalGas => "Zemní plyn",
        FuelType.Lpg => "LPG",
        _ => fuel.ToString(),
    };
}
