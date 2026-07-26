using System.Globalization;
using System.Text;

namespace Camledian.Photobooth.Printing;

/// <summary>
/// Builds a raw ESC/POS byte payload for a QR slip (header text, QR code, footer text) — pure
/// function, no hardware involved, which is what makes the receipt path unit-testable. The QR is
/// sent as a raster bitmap (GS v 0) rather than the native QR command, so it prints even on cheap
/// printers whose firmware lacks 2D-barcode support.
/// </summary>
public static class EscPosPayloadBuilder
{
    private static readonly byte[] Initialize = [0x1B, 0x40]; // ESC @
    private static readonly byte[] AlignCenter = [0x1B, 0x61, 0x01]; // ESC a 1
    private static readonly byte[] DoubleSize = [0x1D, 0x21, 0x11]; // GS ! (2x width + height)
    private static readonly byte[] NormalSize = [0x1D, 0x21, 0x00];

    /// <summary>Builds the complete slip. <paramref name="qrModules"/> is the QR module matrix
    /// (true = dark); <paramref name="paperWidthDots"/> is 384 for 58mm paper, 576 for 80mm.</summary>
    public static byte[] Build(string headerText, string footerText, bool[,] qrModules, int paperWidthDots)
    {
        using var stream = new MemoryStream();

        stream.Write(Initialize);
        stream.Write(AlignCenter);

        if (!string.IsNullOrWhiteSpace(headerText))
        {
            stream.Write(DoubleSize);
            WriteTextLine(stream, headerText);
            stream.Write(NormalSize);
            WriteTextLine(stream, string.Empty);
        }

        WriteQrRaster(stream, qrModules, paperWidthDots);

        if (!string.IsNullOrWhiteSpace(footerText))
        {
            WriteTextLine(stream, string.Empty);
            WriteTextLine(stream, footerText);
        }

        // Feed past the tear bar; 58mm printers usually have no cutter, so no cut command.
        stream.Write([0x1B, 0x64, 0x04]); // ESC d 4 = feed 4 lines

        return stream.ToArray();
    }

    /// <summary>Receipt printers default to codepage 437, which has no Czech accented characters —
    /// transliterate to plain ASCII ("Vaše" -&gt; "Vase") instead of printing mojibake.</summary>
    public static string TransliterateToAscii(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(ch <= 0x7F ? ch : '?');
        }

        return builder.ToString();
    }

    private static void WriteTextLine(MemoryStream stream, string text)
    {
        var ascii = TransliterateToAscii(text);
        stream.Write(Encoding.ASCII.GetBytes(ascii));
        stream.WriteByte(0x0A); // LF prints the line buffer
    }

    /// <summary>GS v 0 raster: the QR matrix scaled up to a comfortable module size (with a quiet
    /// zone), centered by the earlier ESC a 1 — printers center raster images too.</summary>
    private static void WriteQrRaster(MemoryStream stream, bool[,] modules, int paperWidthDots)
    {
        var moduleCount = modules.GetLength(0);
        const int quietZoneModules = 4;
        var totalModules = moduleCount + (2 * quietZoneModules);

        // Biggest whole-number scale that still fits the printable width, clamped to stay scannable.
        var scale = Math.Clamp(paperWidthDots / totalModules, 2, 12);
        var sizeDots = totalModules * scale;
        var widthBytes = (sizeDots + 7) / 8;

        stream.Write([0x1D, 0x76, 0x30, 0x00]); // GS v 0, normal mode
        stream.WriteByte((byte)(widthBytes & 0xFF));
        stream.WriteByte((byte)((widthBytes >> 8) & 0xFF));
        stream.WriteByte((byte)(sizeDots & 0xFF));
        stream.WriteByte((byte)((sizeDots >> 8) & 0xFF));

        var row = new byte[widthBytes];
        for (var y = 0; y < sizeDots; y++)
        {
            Array.Clear(row);
            var moduleY = (y / scale) - quietZoneModules;
            for (var x = 0; x < sizeDots; x++)
            {
                var moduleX = (x / scale) - quietZoneModules;
                var dark = moduleY >= 0 && moduleY < moduleCount &&
                           moduleX >= 0 && moduleX < moduleCount &&
                           modules[moduleY, moduleX];
                if (dark)
                {
                    row[x / 8] |= (byte)(0x80 >> (x % 8));
                }
            }

            stream.Write(row);
        }
    }
}
