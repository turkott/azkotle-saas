using AzKotle.Application.Abstractions;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AzKotle.Infrastructure.QrCodes;

public sealed class BoilerLabelPdfRenderer : IBoilerLabelPdfRenderer
{
    private readonly IQrCodeImageRenderer _qrRenderer;

    public BoilerLabelPdfRenderer(IQrCodeImageRenderer qrRenderer)
    {
        _qrRenderer = qrRenderer;
    }

    public byte[] RenderA4Sheet(IReadOnlyList<BoilerLabel> labels)
    {
        ArgumentNullException.ThrowIfNull(labels);
        if (labels.Count == 0)
        {
            throw new ArgumentException("Aspoň jeden štítek je povinný.", nameof(labels));
        }

        var qrPngs = labels.Select(l => _qrRenderer.RenderPng(l.QrTargetUrl, pixelsPerModule: 12)).ToList();

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(15, Unit.Millimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(t => t.FontSize(10));

                page.Header().Text("AZ KOTLE — QR štítky").SemiBold().FontSize(14);

                page.Content().PaddingVertical(10).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                    });

                    for (var i = 0; i < labels.Count; i++)
                    {
                        var label = labels[i];
                        var png = qrPngs[i];

                        table.Cell().Border(0.5f).Padding(8).Row(row =>
                        {
                            row.ConstantItem(80).Image(png);
                            row.RelativeItem().PaddingLeft(8).Column(info =>
                            {
                                info.Item().Text(label.QrCode).Bold().FontSize(12);
                                info.Item().Text($"{label.Manufacturer} {label.Model}").FontSize(9);
                                info.Item().Text($"S/N: {label.SerialNo}").FontSize(8).FontColor(Colors.Grey.Darken1);
                                info.Item().Text(label.LocationLabel).FontSize(8).FontColor(Colors.Grey.Darken1);
                            });
                        });
                    }
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("Vygenerováno ").FontSize(8).FontColor(Colors.Grey.Medium);
                    t.Span(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'")).FontSize(8).FontColor(Colors.Grey.Medium);
                });
            });
        });

        return document.GeneratePdf();
    }
}
