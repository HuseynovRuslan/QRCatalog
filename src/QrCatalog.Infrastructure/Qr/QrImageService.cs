using QRCoder;

namespace QrCatalog.Infrastructure.Qr;

/// <summary>
/// QR şəkil generasiyası. ECC səviyyəsi H (30% zədə bərpası) — etiket açıq havada
/// cızılandan, solandan sonra da oxunmalıdır. Quiet zone QRCoder-in öz kənarı ilə gəlir.
/// </summary>
public sealed class QrImageService
{
    public string RenderSvg(string url, int sizePx = 512)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.H);
        var svg = new SvgQRCode(data);
        return svg.GetGraphic(sizePx, darkColorHex: "#000000", lightColorHex: "#FFFFFF");
    }

    public byte[] RenderPng(string url, int pixelsPerModule = 16)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.H);
        var png = new PngByteQRCode(data);
        return png.GetGraphic(pixelsPerModule);
    }
}
