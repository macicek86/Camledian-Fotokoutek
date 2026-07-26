using Camledian.Photobooth.Printing;

namespace Camledian.Photobooth.Tests.Printing;

public class EscPosPayloadBuilderTests
{
    private static bool[,] MakeCheckerboardModules(int size)
    {
        var modules = new bool[size, size];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                modules[y, x] = (x + y) % 2 == 0;
            }
        }

        return modules;
    }

    [Fact]
    public void Build_StartsWithInitAndCenterAlign()
    {
        var payload = EscPosPayloadBuilder.Build("Header", "Footer", MakeCheckerboardModules(21), 384);

        Assert.Equal(0x1B, payload[0]);
        Assert.Equal(0x40, payload[1]);
        Assert.Equal(0x1B, payload[2]);
        Assert.Equal(0x61, payload[3]);
        Assert.Equal(0x01, payload[4]);
    }

    [Fact]
    public void Build_EndsWithFeedCommand()
    {
        var payload = EscPosPayloadBuilder.Build("Header", "Footer", MakeCheckerboardModules(21), 384);

        Assert.Equal(0x1B, payload[^3]);
        Assert.Equal(0x64, payload[^2]);
        Assert.Equal(0x04, payload[^1]);
    }

    [Fact]
    public void Build_ContainsTransliteratedHeaderAndFooterText()
    {
        var payload = EscPosPayloadBuilder.Build("Vaše fotografie", "Naskenujte QR kód", MakeCheckerboardModules(21), 384);
        var text = System.Text.Encoding.ASCII.GetString(payload);

        Assert.Contains("Vase fotografie", text);
        Assert.Contains("Naskenujte QR kod", text);
    }

    [Fact]
    public void Build_WithoutHeaderOrFooter_OmitsTextLines()
    {
        var withText = EscPosPayloadBuilder.Build("Header", "Footer", MakeCheckerboardModules(21), 384);
        var withoutText = EscPosPayloadBuilder.Build(string.Empty, string.Empty, MakeCheckerboardModules(21), 384);

        Assert.True(withoutText.Length < withText.Length);
    }

    [Theory]
    [InlineData(21, 384)] // typical QR version 1 on 58mm paper
    [InlineData(25, 576)] // slightly bigger QR on 80mm paper
    public void Build_QrRaster_HeaderDescribesActualBitmapSize(int moduleCount, int paperWidthDots)
    {
        var payload = EscPosPayloadBuilder.Build(string.Empty, string.Empty, MakeCheckerboardModules(moduleCount), paperWidthDots);

        var commandIndex = FindSequence(payload, [0x1D, 0x76, 0x30, 0x00]);
        Assert.True(commandIndex >= 0, "GS v 0 raster command not found in payload.");

        var widthBytes = payload[commandIndex + 4] | (payload[commandIndex + 5] << 8);
        var heightDots = payload[commandIndex + 6] | (payload[commandIndex + 7] << 8);

        const int quietZoneModules = 4;
        var totalModules = moduleCount + (2 * quietZoneModules);
        var expectedScale = Math.Clamp(paperWidthDots / totalModules, 2, 12);
        var expectedSizeDots = totalModules * expectedScale;
        var expectedWidthBytes = (expectedSizeDots + 7) / 8;

        Assert.Equal(expectedSizeDots, heightDots);
        Assert.Equal(expectedWidthBytes, widthBytes);

        var rasterBytes = widthBytes * heightDots;
        var rasterStart = commandIndex + 8;
        Assert.True(rasterStart + rasterBytes <= payload.Length, "Raster body shorter than the declared bitmap size.");
    }

    [Fact]
    public void TransliterateToAscii_StripsCzechDiacritics()
    {
        Assert.Equal("Vase fotografie", EscPosPayloadBuilder.TransliterateToAscii("Vaše fotografie"));
        Assert.Equal("Prilis zlutoucky kun upel dabelske ody",
            EscPosPayloadBuilder.TransliterateToAscii("Příliš žluťoučký kůň úpěl ďábelské ódy"));
    }

    [Fact]
    public void TransliterateToAscii_ReplacesUnmappableCharactersWithQuestionMark()
    {
        Assert.Equal("?? ok", EscPosPayloadBuilder.TransliterateToAscii("😊 ok"));
    }

    private static int FindSequence(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return i;
            }
        }

        return -1;
    }
}
