namespace AzKotle.Application.Abstractions;

public interface IQrCodeImageRenderer
{
    byte[] RenderPng(string content, int pixelsPerModule = 10);
}

public interface IBoilerLabelPdfRenderer
{
    byte[] RenderA4Sheet(IReadOnlyList<BoilerLabel> labels);
}

public sealed record BoilerLabel(
    string QrCode,
    string QrTargetUrl,
    string Manufacturer,
    string Model,
    string SerialNo,
    string LocationLabel);
