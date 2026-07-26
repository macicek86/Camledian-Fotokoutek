using Camledian.Photobooth.Core.Models;
using Camledian.Photobooth.Imaging.BackgroundSubtraction;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Camledian.Photobooth.Tests.Imaging;

public class BackgroundSubtractionProcessorTests
{
    private static BackgroundSubtractionSettings DefaultSettings() => new()
    {
        ThresholdDistance = 40,
        FeatherPixels = 0, // disabled for pixel-exact assertions in most tests
    };

    private static Image<Rgba32> Solid(int size, Rgba32 color)
    {
        var image = new Image<Rgba32>(size, size);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < size; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < size; x++)
                {
                    row[x] = color;
                }
            }
        });
        return image;
    }

    [Fact]
    public void IdenticalFrameAndReferenceAreFullyBackground()
    {
        using var reference = Solid(16, new Rgba32(80, 120, 200));
        using var frame = Solid(16, new Rgba32(80, 120, 200));

        var mask = BackgroundSubtractionProcessor.Apply(frame, reference, DefaultSettings());

        Assert.All(mask, m => Assert.True(m < 0.05f, $"expected near-0 alpha for an unchanged scene, got {m}"));
    }

    [Fact]
    public void DistinctlyDifferentColorIsForeground()
    {
        using var reference = Solid(16, new Rgba32(20, 20, 20)); // dark empty room
        using var frame = Solid(16, new Rgba32(230, 190, 160)); // a person's skin tone standing in front

        var mask = BackgroundSubtractionProcessor.Apply(frame, reference, DefaultSettings());

        Assert.All(mask, m => Assert.True(m > 0.95f, $"expected near-1 alpha for a clearly different subject, got {m}"));
    }

    [Fact]
    public void SubtleLightingDriftBelowThresholdStaysBackground()
    {
        using var reference = Solid(16, new Rgba32(100, 100, 100));
        using var frame = Solid(16, new Rgba32(108, 103, 105)); // small drift, well under the threshold

        var settings = DefaultSettings();
        settings.ThresholdDistance = 40;
        var mask = BackgroundSubtractionProcessor.Apply(frame, reference, settings);

        Assert.All(mask, m => Assert.True(m < 0.05f, "small lighting drift should not be flagged as foreground"));
    }

    [Fact]
    public void LowerThresholdIsMoreSensitiveToTheSameDrift()
    {
        using var reference = Solid(16, new Rgba32(100, 100, 100));
        using var frame = Solid(16, new Rgba32(108, 103, 105));

        var sensitiveSettings = DefaultSettings();
        sensitiveSettings.ThresholdDistance = 5;
        var mask = BackgroundSubtractionProcessor.Apply(frame, reference, sensitiveSettings);

        Assert.All(mask, m => Assert.True(m > 0.95f, "a low threshold should flag even small drift as foreground"));
    }

    [Fact]
    public void MismatchedReferenceSizeIsResizedAndStillWorks()
    {
        using var reference = Solid(8, new Rgba32(10, 10, 10)); // smaller than the frame
        using var frame = Solid(32, new Rgba32(240, 240, 240));

        var mask = BackgroundSubtractionProcessor.Apply(frame, reference, DefaultSettings());

        Assert.Equal(32 * 32, mask.Length);
        Assert.All(mask, m => Assert.True(m > 0.95f));
    }

    [Fact]
    public void FeatherProducesIntermediateAlphaAtBoundary()
    {
        var settings = DefaultSettings();
        settings.FeatherPixels = 4;

        using var reference = Solid(40, new Rgba32(10, 10, 10));
        using var frame = new Image<Rgba32>(40, 10);
        frame.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < frame.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < frame.Width; x++)
                {
                    row[x] = x < frame.Width / 2 ? new Rgba32(10, 10, 10) : new Rgba32(240, 240, 240);
                }
            }
        });

        var mask = BackgroundSubtractionProcessor.Apply(frame, reference, settings);

        Assert.Contains(mask, m => m > 0.05f && m < 0.95f);
    }
}
