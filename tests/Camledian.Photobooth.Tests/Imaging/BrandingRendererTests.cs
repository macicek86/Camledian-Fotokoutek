using Camledian.Photobooth.Core.Models;
using Camledian.Photobooth.Imaging.Branding;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Camledian.Photobooth.Tests.Imaging;

public class BrandingRendererTests
{
    private static Image<Rgba32> Solid(int w, int h, Rgba32 color)
    {
        var image = new Image<Rgba32>(w, h);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < h; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < w; x++)
                {
                    row[x] = color;
                }
            }
        });
        return image;
    }

    private static readonly Rgba32 Base = new(10, 10, 10);

    private static int CountChangedPixels(Image<Rgba32> image)
    {
        var changed = 0;
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < image.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < image.Width; x++)
                {
                    if (row[x] != Base)
                    {
                        changed++;
                    }
                }
            }
        });
        return changed;
    }

    [Fact]
    public void Disabled_LeavesImageUntouched()
    {
        using var image = Solid(200, 100, Base);
        BrandingRenderer.Apply(image, new BrandingSettings { Enabled = false, BannerText = "Test" });

        Assert.Equal(0, CountChangedPixels(image));
    }

    [Fact]
    public void BannerText_DrawsSomethingIntoTheBannerArea()
    {
        using var image = Solid(400, 200, Base);
        BrandingRenderer.Apply(image, new BrandingSettings
        {
            Enabled = true,
            BannerText = "Demo Event 2026",
            BannerPosition = BannerPosition.Bottom,
            BannerBackgroundHex = "#FFFFFFFF",
        });

        // The white bar alone guarantees a large changed region at the bottom.
        Assert.True(CountChangedPixels(image) > 400, "expected the banner bar/text to visibly change pixels");
    }

    [Fact]
    public void MissingLogoFile_IsSkippedWithoutThrowing()
    {
        using var image = Solid(200, 100, Base);
        BrandingRenderer.Apply(image, new BrandingSettings
        {
            Enabled = true,
            LogoPath = "/nonexistent/logo.png",
        });

        Assert.Equal(0, CountChangedPixels(image));
    }

    [Fact]
    public void Logo_IsPlacedInTheConfiguredCorner()
    {
        var logoPath = Path.Combine(Path.GetTempPath(), $"branding-test-logo-{Guid.NewGuid():N}.png");
        try
        {
            using (var logo = Solid(50, 50, new Rgba32(255, 0, 0)))
            {
                logo.SaveAsPng(logoPath);
            }

            using var image = Solid(400, 200, Base);
            BrandingRenderer.Apply(image, new BrandingSettings
            {
                Enabled = true,
                LogoPath = logoPath,
                LogoCorner = LogoCorner.TopLeft,
                LogoWidthPercent = 10,
                LogoMarginPercent = 2,
            });

            // 10% of 400px = 40px logo at ~8px margin: pixel (20, 20) is inside it, opposite corner isn't.
            Assert.Equal(new Rgba32(255, 0, 0), image[20, 20]);
            Assert.Equal(Base, image[380, 180]);
        }
        finally
        {
            File.Delete(logoPath);
        }
    }

    [Fact]
    public void InvalidColorHex_FallsBackInsteadOfThrowing()
    {
        using var image = Solid(200, 100, Base);
        var settings = new BrandingSettings
        {
            Enabled = true,
            BannerText = "Test",
            TextColorHex = "not-a-color",
            BannerBackgroundHex = "also-not-a-color",
        };

        var exception = Record.Exception(() => BrandingRenderer.Apply(image, settings));

        Assert.Null(exception);
    }
}
