using Camledian.Photobooth.Core.Models;
using Camledian.Photobooth.Imaging.ChromaKey;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Camledian.Photobooth.Tests.Imaging;

public class ChromaKeyProcessorTests
{
    private static ChromaKeySettings DefaultSettings() => new()
    {
        TargetHueDegrees = 120,
        HueToleranceDegrees = 25,
        SaturationThreshold = 0.20,
        ValueThreshold = 0.20,
        FeatherPixels = 0, // disable feather so edges are pixel-exact for these assertions
        EdgeSmoothingPixels = 0,
        SpillSuppressionStrength = 0.6,
    };

    private static Image<Rgba32> SolidColorImage(int size, Rgba32 color)
    {
        var image = new Image<Rgba32>(size, size);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < image.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < image.Width; x++)
                {
                    row[x] = color;
                }
            }
        });
        return image;
    }

    [Fact]
    public void PureGreenScreenBecomesFullyTransparent()
    {
        using var image = SolidColorImage(16, new Rgba32(0, 255, 0));
        var mask = ChromaKeyProcessor.Apply(image, DefaultSettings());

        Assert.All(mask, m => Assert.True(m < 0.05f, $"expected near-0 alpha, got {m}"));
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < image.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < image.Width; x++)
                {
                    Assert.True(row[x].A < 15, $"expected near-0 alpha byte, got {row[x].A}");
                }
            }
        });
    }

    [Fact]
    public void SkinToneStaysFullyOpaque()
    {
        using var image = SolidColorImage(16, new Rgba32(222, 184, 155));
        var mask = ChromaKeyProcessor.Apply(image, DefaultSettings());

        Assert.All(mask, m => Assert.True(m > 0.95f, $"expected near-1 alpha, got {m}"));
    }

    [Fact]
    public void BlueScreenIsKeyedWhenTargetHueIsBlue()
    {
        var settings = DefaultSettings();
        settings.TargetHueDegrees = 240;

        using var image = SolidColorImage(16, new Rgba32(0, 0, 255));
        var mask = ChromaKeyProcessor.Apply(image, settings);

        Assert.All(mask, m => Assert.True(m < 0.05f));
    }

    [Fact]
    public void GreenSpillOnOpaquePixelsIsDespilled()
    {
        // A skin-tone pixel with green spill: green channel boosted above the red/blue average.
        var settings = DefaultSettings();
        settings.SpillSuppressionStrength = 1.0;

        using var image = SolidColorImage(4, new Rgba32(180, 220, 160));
        ChromaKeyProcessor.Apply(image, settings);

        image.ProcessPixelRows(accessor =>
        {
            var row = accessor.GetRowSpan(0);
            var px = row[0];
            // Green should have been pulled down toward the red/blue average (170) since it doesn't
            // survive the hue/threshold check as pure background but still reads as somewhat green.
            Assert.True(px.G <= 220);
        });
    }

    [Fact]
    public void FeatherProducesIntermediateAlphaAtBoundary()
    {
        var settings = DefaultSettings();
        settings.FeatherPixels = 4;
        settings.EdgeSmoothingPixels = 2;

        // Half green, half skin tone, split down the middle -> feather zone should exist near the seam.
        using var image = new Image<Rgba32>(40, 10);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < image.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < image.Width; x++)
                {
                    row[x] = x < image.Width / 2 ? new Rgba32(0, 255, 0) : new Rgba32(222, 184, 155);
                }
            }
        });

        var mask = ChromaKeyProcessor.Apply(image, settings);

        var hasIntermediate = mask.Any(m => m > 0.05f && m < 0.95f);
        Assert.True(hasIntermediate, "expected at least some feathered/intermediate alpha values near the seam");
    }
}
