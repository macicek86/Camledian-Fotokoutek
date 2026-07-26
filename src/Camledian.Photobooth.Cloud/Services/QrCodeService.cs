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

    /// <summary>Raw module matrix (true = dark), for renderers that draw the QR themselves — e.g.
    /// the ESC/POS raster sent to the thermal receipt printer.</summary>
    public static bool[,] GenerateModules(string content)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q);

        var size = data.ModuleMatrix.Count;
        var modules = new bool[size, size];
        for (var y = 0; y < size; y++)
        {
            var row = data.ModuleMatrix[y];
            for (var x = 0; x < size; x++)
            {
                modules[y, x] = row[x];
            }
        }

        return modules;
    }
}
