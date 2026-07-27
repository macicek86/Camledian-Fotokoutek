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

    private static void Fill(Image<Rgba32> image, Rectangle area, Rgba32 color)
    {
        image.ProcessPixelRows(accessor =>
        {
            for (var y = area.Top; y < area.Bottom; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = area.Left; x < area.Right; x++)
                {
                    row[x] = color;
                }
            }
        });
    }

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

    /// <summary>Sensitivity is about *local* differences — a subject that barely stands out from the
    /// background. A frame-wide shift is deliberately not detectable at any threshold now that drift
    /// compensation cancels it (see <see cref="GlobalExposureShiftIsCompensatedAndStaysBackground"/>),
    /// so this uses a patch instead of a uniform frame.</summary>
    [Fact]
    public void LowerThresholdIsMoreSensitiveToAFaintSubject()
    {
        using var reference = Solid(64, new Rgba32(100, 100, 100));
        using var frame = Solid(64, new Rgba32(100, 100, 100));
        Fill(frame, new Rectangle(20, 20, 16, 16), new Rgba32(108, 103, 105)); // barely-there subject

        var defaultMask = BackgroundSubtractionProcessor.Apply(frame.Clone(), reference, DefaultSettings());
        Assert.True(defaultMask[(28 * 64) + 28] < 0.05f, "the default threshold should ignore such a faint difference");

        var sensitiveSettings = DefaultSettings();
        sensitiveSettings.ThresholdDistance = 5;
        var sensitiveMask = BackgroundSubtractionProcessor.Apply(frame, reference, sensitiveSettings);
        Assert.True(sensitiveMask[(28 * 64) + 28] > 0.95f, "a low threshold should flag even a faint subject");
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

    /// <summary>The webcam re-meters the scene the moment someone walks in: every pixel gets
    /// brighter at once. Without compensation that reads as "the entire frame is foreground".</summary>
    [Fact]
    public void GlobalExposureShiftIsCompensatedAndStaysBackground()
    {
        using var reference = Solid(64, new Rgba32(120, 120, 120));
        using var frame = Solid(64, new Rgba32(156, 156, 156)); // +30 %, distance 62 — well over the threshold

        var mask = BackgroundSubtractionProcessor.Apply(frame, reference, DefaultSettings());

        Assert.All(mask, m => Assert.True(m < 0.05f, $"a pure exposure shift should not become foreground, got {m}"));
    }

    [Fact]
    public void ExposureCompensationCanBeTurnedOff()
    {
        using var reference = Solid(64, new Rgba32(120, 120, 120));
        using var frame = Solid(64, new Rgba32(156, 156, 156));

        var settings = DefaultSettings();
        settings.CompensateLightingDrift = false;
        var mask = BackgroundSubtractionProcessor.Apply(frame, reference, settings);

        Assert.All(mask, m => Assert.True(m > 0.95f, "with compensation off the old behaviour is kept"));
    }

    /// <summary>The compensation must not "explain away" the subject itself: a person occupying part
    /// of a frame that also drifted has to stay foreground.</summary>
    [Fact]
    public void SubjectIsStillDetectedInsideAnExposureShiftedFrame()
    {
        using var reference = Solid(64, new Rgba32(120, 120, 120));
        using var frame = Solid(64, new Rgba32(156, 156, 156));
        Fill(frame, new Rectangle(8, 8, 24, 48), new Rgba32(230, 60, 40)); // subject covering ~28 % of the frame

        var mask = BackgroundSubtractionProcessor.Apply(frame, reference, DefaultSettings());

        Assert.True(mask[(20 * 64) + 20] > 0.95f, "the subject must remain foreground");
        Assert.True(mask[(20 * 64) + 55] < 0.05f, "the drifted background must remain background");
    }

    /// <summary>
    /// A guest standing right up against the lens: the drift estimate can only see a sliver of real
    /// background, and the first version of it took the median of everything — which described the
    /// guest, rescaled the reference to match them, and made the actual background stop matching
    /// (35 % of it was then wrongly kept). The estimate must recognise it has nothing to measure and
    /// leave the reference alone.
    /// </summary>
    [Fact]
    public void SubjectFillingMostOfTheFrameDoesNotBreakTheBackground()
    {
        using var reference = Solid(64, new Rgba32(90, 100, 110));
        using var frame = Solid(64, new Rgba32(90, 100, 110));
        Fill(frame, new Rectangle(0, 0, 64, 52), new Rgba32(210, 180, 150)); // subject over ~81 % of the frame

        var mask = BackgroundSubtractionProcessor.Apply(frame, reference, DefaultSettings());

        Assert.True(mask[(20 * 64) + 32] > 0.95f, "the subject must be foreground");
        Assert.True(mask[(62 * 64) + 32] < 0.05f, "the strip of real background must stay background");
    }

    /// <summary>A prop is just "something that wasn't in the reference photo" — the method has no
    /// idea what it is, and that is exactly why it keeps it when a person-shaped model wouldn't.</summary>
    [Fact]
    public void PropSeparateFromTheSubjectIsKept()
    {
        using var reference = Solid(64, new Rgba32(90, 110, 130));
        using var frame = Solid(64, new Rgba32(90, 110, 130));
        Fill(frame, new Rectangle(10, 10, 20, 40), new Rgba32(240, 230, 200)); // person
        Fill(frame, new Rectangle(45, 20, 6, 6), new Rgba32(200, 40, 40));     // prop held out to the side

        var mask = BackgroundSubtractionProcessor.Apply(frame, reference, DefaultSettings());

        Assert.True(mask[(22 * 64) + 47] > 0.95f, "a prop away from the body must survive");
    }

    /// <summary>Where clothing matches the background behind it the raw difference is zero, punching a
    /// hole through the middle of a guest. Closing fills those without touching the outline.</summary>
    [Fact]
    public void HoleInsideTheSubjectIsFilled()
    {
        using var reference = Solid(64, new Rgba32(60, 60, 60));
        using var frame = Solid(64, new Rgba32(60, 60, 60));
        Fill(frame, new Rectangle(16, 16, 32, 32), new Rgba32(230, 200, 180)); // subject
        Fill(frame, new Rectangle(30, 30, 3, 3), new Rgba32(60, 60, 60));      // clothing matching the background

        var settings = DefaultSettings();
        settings.FillHolesPixels = 2;
        var mask = BackgroundSubtractionProcessor.Apply(frame, reference, settings);

        Assert.True(mask[(31 * 64) + 31] > 0.95f, "the pinhole inside the subject should be closed");
    }

    [Fact]
    public void HoleFillingDoesNotBleedIntoTheBackground()
    {
        using var reference = Solid(64, new Rgba32(60, 60, 60));
        using var frame = Solid(64, new Rgba32(60, 60, 60));
        Fill(frame, new Rectangle(16, 16, 32, 32), new Rgba32(230, 200, 180));

        var settings = DefaultSettings();
        settings.FillHolesPixels = 2;
        var mask = BackgroundSubtractionProcessor.Apply(frame, reference, settings);

        Assert.True(mask[(5 * 64) + 5] < 0.05f, "background far from the subject must stay background");
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
