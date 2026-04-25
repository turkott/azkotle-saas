using AzKotle.Application.Abstractions;
using AzKotle.Domain.Entities.Inspections;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AzKotle.Infrastructure.Pdf;

public sealed class InspectionReportPdfRenderer : IInspectionReportPdfRenderer
{
    private static readonly string[] _typeLabels =
    {
        string.Empty,
        "Roční prohlídka spotřebiče (NV 191/2022 Sb.)",
        "Servis plynového zařízení (TPG 704 01)",
        "Mimořádná kontrola",
    };

    public byte[] Render(InspectionReportData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(20, Unit.Millimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(t => t.FontSize(10).FontColor(Colors.Black));

                page.Header().Element(h => Header(h, data));
                page.Content().PaddingVertical(8).Element(c => Body(c, data));
                page.Footer().Element(f => Footer(f, data));
            });
        });

        return document.GeneratePdf();
    }

    private static void Header(IContainer container, InspectionReportData data)
    {
        container.Column(col =>
        {
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text(data.Tenant.CompanyName).Bold().FontSize(14);
                    if (!string.IsNullOrWhiteSpace(data.Tenant.Ico))
                    {
                        left.Item().Text($"IČO: {data.Tenant.Ico}").FontSize(9).FontColor(Colors.Grey.Darken1);
                    }
                    if (!string.IsNullOrWhiteSpace(data.Tenant.Dic))
                    {
                        left.Item().Text($"DIČ: {data.Tenant.Dic}").FontSize(9).FontColor(Colors.Grey.Darken1);
                    }
                });
                row.ConstantItem(160).AlignRight().Column(right =>
                {
                    right.Item().Text("REVIZNÍ ZPRÁVA").Bold().FontSize(13).FontColor(Colors.Black);
                    right.Item().Text(TypeLabel(data.Type)).FontSize(8).FontColor(Colors.Grey.Darken1);
                    right.Item().Text($"Č. {data.InspectionNumber}").FontSize(9);
                });
            });

            col.Item().PaddingTop(4).LineHorizontal(1f).LineColor(Colors.Grey.Lighten2);
        });
    }

    private static void Body(IContainer container, InspectionReportData data)
    {
        container.Column(col =>
        {
            col.Spacing(10);

            col.Item().Row(row =>
            {
                row.RelativeItem().Element(c => InfoCard(c, "Zákazník", new[]
                {
                    ("Jméno / Firma", data.Customer.Name),
                    ("IČO", data.Customer.Ico ?? "—"),
                    ("Email", data.Customer.Email ?? "—"),
                    ("Telefon", data.Customer.Phone ?? "—"),
                }));
                row.ConstantItem(8);
                row.RelativeItem().Element(c => InfoCard(c, "Lokalita", new[]
                {
                    ("Ulice", data.Location.Street),
                    ("Město", data.Location.City),
                    ("PSČ", data.Location.Zip),
                }));
            });

            col.Item().Element(c => InfoCard(c, "Kotel", new[]
            {
                ("QR kód", data.Boiler.QrCode),
                ("Výrobce", data.Boiler.Manufacturer),
                ("Model", data.Boiler.Model),
                ("Sériové č.", data.Boiler.SerialNo),
                ("Výkon", $"{data.Boiler.OutputKw:F1} kW"),
                ("Palivo", data.Boiler.FuelType),
                ("Instalace", data.Boiler.InstalledAt.ToString("dd.MM.yyyy")),
                ("Datum revize", data.PerformedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm")),
                ("Další termín", data.NextDueAt?.ToString("dd.MM.yyyy") ?? "—"),
            }));

            foreach (var section in data.FormSections)
            {
                col.Item().Element(c => SectionCard(c, section));
            }

            if (!string.IsNullOrWhiteSpace(data.Findings))
            {
                col.Item().Element(c => TextBlock(c, "Závady", data.Findings!));
            }
            if (!string.IsNullOrWhiteSpace(data.Recommendations))
            {
                col.Item().Element(c => TextBlock(c, "Doporučení", data.Recommendations!));
            }

            col.Item().PaddingTop(20).Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().PaddingTop(20).LineHorizontal(0.5f).LineColor(Colors.Grey.Darken1);
                    c.Item().Text("Podpis technika").FontSize(8).FontColor(Colors.Grey.Darken1);
                    c.Item().Text(data.Technician.FullName).FontSize(10);
                    if (!string.IsNullOrWhiteSpace(data.Technician.LicenseNo))
                    {
                        c.Item().Text($"Osvědčení TIČR: {data.Technician.LicenseNo}").FontSize(8).FontColor(Colors.Grey.Darken1);
                    }
                });
                row.ConstantItem(20);
                row.RelativeItem().Column(c =>
                {
                    c.Item().PaddingTop(20).LineHorizontal(0.5f).LineColor(Colors.Grey.Darken1);
                    c.Item().Text("Podpis provozovatele").FontSize(8).FontColor(Colors.Grey.Darken1);
                });
            });
        });
    }

    private static void InfoCard(IContainer container, string title, IReadOnlyList<(string Label, string Value)> rows)
    {
        container.Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(10).Column(col =>
        {
            col.Item().Text(title).Bold().FontSize(11);
            col.Item().PaddingTop(4).Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(2);
                    c.RelativeColumn(3);
                });
                foreach (var (label, value) in rows)
                {
                    table.Cell().PaddingVertical(2).Text(label).FontSize(9).FontColor(Colors.Grey.Darken2);
                    table.Cell().PaddingVertical(2).Text(value ?? "—").FontSize(9);
                }
            });
        });
    }

    private static void SectionCard(IContainer container, InspectionReportSection section)
    {
        container.Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(10).Column(col =>
        {
            col.Item().Text(section.Title).Bold().FontSize(11);
            col.Item().PaddingTop(4).Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(2);
                    c.RelativeColumn(3);
                });
                if (section.Fields.Count == 0)
                {
                    table.Cell().ColumnSpan(2).Text("(bez údajů)").FontSize(9).FontColor(Colors.Grey.Darken1);
                    return;
                }
                foreach (var f in section.Fields)
                {
                    table.Cell().PaddingVertical(2).Text(f.Label).FontSize(9).FontColor(Colors.Grey.Darken2);
                    table.Cell().PaddingVertical(2).Text(string.IsNullOrWhiteSpace(f.DisplayValue) ? "—" : f.DisplayValue).FontSize(9);
                }
            });
        });
    }

    private static void TextBlock(IContainer container, string title, string text)
    {
        container.Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(10).Column(col =>
        {
            col.Item().Text(title).Bold().FontSize(11);
            col.Item().PaddingTop(4).Text(text).FontSize(10);
        });
    }

    private static void Footer(IContainer container, InspectionReportData data)
    {
        container.AlignCenter().Text(t =>
        {
            t.Span("Vygenerováno systémem AZ KOTLE • ").FontSize(8).FontColor(Colors.Grey.Medium);
            t.Span(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'")).FontSize(8).FontColor(Colors.Grey.Medium);
            t.Span(" • Zpráva č. ").FontSize(8).FontColor(Colors.Grey.Medium);
            t.Span(data.InspectionNumber).FontSize(8).FontColor(Colors.Grey.Medium);
        });
    }

    private static string TypeLabel(InspectionType type)
    {
        var idx = (int)type;
        return idx >= 0 && idx < _typeLabels.Length ? _typeLabels[idx] : type.ToString();
    }
}
