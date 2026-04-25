using AzKotle.Application.Abstractions;
using QRCoder;

namespace AzKotle.Infrastructure.QrCodes;

public sealed class QrCoderImageRenderer : IQrCodeImageRenderer
{
    public byte[] RenderPng(string content, int pixelsPerModule = 10)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        if (pixelsPerModule is < 1 or > 40)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelsPerModule),
                "Pixels per module musí být v rozsahu 1–40.");
        }

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q);
        using var png = new PngByteQRCode(data);
        return png.GetGraphic(pixelsPerModule);
    }
}
