using QRCoder;

namespace Camledian.Photobooth.Cloud.Services;

/// <summary>Generates the QR code shown on the result screen once a photo has an upload/download
/// token (spec §40). Returns raw PNG bytes — pure managed, no System.Drawing dependency, so this
/// stays usable outside the WPF app too (e.g. in tests).</summary>
public static class QrCodeService
{
    public static byte[] GeneratePng(string content, int pixelsPerModule = 12)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q);
        var pngQrCode = new PngByteQRCode(data);
        return pngQrCode.GetGraphic(pixelsPerModule);
    }
}
