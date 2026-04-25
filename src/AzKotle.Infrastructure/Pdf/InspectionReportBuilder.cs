using System.Text.Json;
using AzKotle.Application.Abstractions;
using AzKotle.Domain.Common;
using AzKotle.Domain.Entities.Boilers;
using AzKotle.Domain.Entities.Inspections;
using AzKotle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AzKotle.Infrastructure.Pdf;

public sealed class InspectionReportBuilder
{
    private readonly AzKotleDbContext _db;
    private readonly IInspectionReportPdfRenderer _renderer;

    public InspectionReportBuilder(AzKotleDbContext db, IInspectionReportPdfRenderer renderer)
    {
        _db = db;
        _renderer = renderer;
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
            FormSections: ParseFormSections(inspection.FormDataJson),
            Findings: inspection.Findings,
            Recommendations: inspection.Recommendations);

        return _renderer.Render(data);
    }

    public static string ShortNumber(InspectionId id, DateTime performedAt) =>
        $"{performedAt:yyyy}-{id.Value.ToString("N")[..8].ToUpperInvariant()}";

    private static IReadOnlyList<InspectionReportSection> ParseFormSections(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
        {
            return Array.Empty<InspectionReportSection>();
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Array.Empty<InspectionReportSection>();
            }

            var fields = new List<InspectionReportField>();
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                fields.Add(new InspectionReportField(
                    Label: HumanizeKey(property.Name),
                    DisplayValue: FormatValue(property.Value)));
            }
            return fields.Count == 0
                ? Array.Empty<InspectionReportSection>()
                : new[] { new InspectionReportSection("Vyplněné údaje", fields) };
        }
        catch (JsonException)
        {
            return Array.Empty<InspectionReportSection>();
        }
    }

    private static string HumanizeKey(string key)
    {
        var trimmed = key.Replace('_', ' ').Trim();
        return trimmed.Length switch
        {
            0 => key,
            1 => trimmed.ToUpperInvariant(),
            _ => char.ToUpperInvariant(trimmed[0]) + trimmed[1..],
        };
    }

    private static string FormatValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "Ano",
        JsonValueKind.False => "Ne",
        JsonValueKind.Null => "—",
        _ => value.GetRawText(),
    };

    private static string FuelLabel(FuelType fuel) => fuel switch
    {
        FuelType.NaturalGas => "Zemní plyn",
        FuelType.Lpg => "LPG",
        _ => fuel.ToString(),
    };
}
