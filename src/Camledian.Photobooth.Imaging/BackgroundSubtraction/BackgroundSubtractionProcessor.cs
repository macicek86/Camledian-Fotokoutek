using Camledian.Photobooth.Core.Models;
using Camledian.Photobooth.Imaging.ChromaKey;
using Camledian.Photobooth.Imaging.PixelMath;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Camledian.Photobooth.Imaging.BackgroundSubtraction;

/// <summary>
/// Reference-photo background subtraction: a pixel counts as foreground when it differs enough
/// (Euclidean RGB distance) from the corresponding pixel in a one-time "empty scene" reference
/// photo. Doesn't need a green screen at all — works for any static background, since the booth and
/// camera don't move during an event. Same feather/smoothstep treatment as the chroma key so edges
/// aren't a jagged binary cutout.
/// </summary>
public static class BackgroundSubtractionProcessor
{
    public static float[] Apply(Image<Rgba32> frame, Image<Rgba32> reference, BackgroundSubtractionSettings settings)
    {
        var width = frame.Width;
        var height = frame.Height;

        // The reference is normally captured at the same camera resolution, but resize defensively
        // in case settings (or the camera) changed between the reference capture and now.
        Image<Rgba32>? resizedReference = null;
        var referenceToUse = reference;
        if (reference.Width != width || reference.Height != height)
        {
            resizedReference = reference.Clone(ctx => ctx.Resize(width, height));
            referenceToUse = resizedReference;
        }

        try
        {
            var mask = new float[width * height];
            var maxDistance = Math.Sqrt(3 * 255.0 * 255.0);
            var thresholdFraction = Math.Clamp(settings.ThresholdDistance / maxDistance, 0.001, 1.0);

            var gain = settings.CompensateLightingDrift
                ? EstimateChannelGain(frame, referenceToUse)
                : (R: 1.0, G: 1.0, B: 1.0);

            frame.ProcessPixelRows(referenceToUse, (frameAccessor, refAccessor) =>
            {
                for (var y = 0; y < height; y++)
                {
                    var frameRow = frameAccessor.GetRowSpan(y);
                    var refRow = refAccessor.GetRowSpan(y);
                    for (var x = 0; x < width; x++)
                    {
                        var f = frameRow[x];
                        var r = refRow[x];
                        var dr = f.R - (r.R * gain.R);
                        var dg = f.G - (r.G * gain.G);
                        var db = f.B - (r.B * gain.B);
                        var distance = Math.Sqrt((dr * dr) + (dg * dg) + (db * db));
                        var normalized = distance / maxDistance;

                        var idx = (y * width) + x;
                        mask[idx] = normalized > thresholdFraction ? 1f : 0f;
                    }
                }
            });

            // Close the pinholes first, then feather: blurring a hole-ridden mask only turns crisp
            // holes into soft grey ones.
            Morphology.Close(mask, width, height, settings.FillHolesPixels);
            BoxBlur.Apply(mask, width, height, settings.FeatherPixels);

            frame.ProcessPixelRows(accessor =>
            {
                for (var y = 0; y < height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (var x = 0; x < width; x++)
                    {
                        var idx = (y * width) + x;
                        var alpha = ColorMath.Clamp01(ColorMath.SmoothStep(0.15, 0.85, mask[idx]));
                        mask[idx] = (float)alpha;
                        ref var px = ref row[x];
                        px.A = (byte)Math.Clamp((int)Math.Round(alpha * 255.0), 0, 255);
                    }
                }
            });

            return mask;
        }
        finally
        {
            resizedReference?.Dispose();
        }
    }

    /// <summary>
    /// Per-channel factor that maps the reference photo onto the current frame's exposure/white
    /// balance, estimated as the *median* frame/reference ratio over a subsample of the image.
    /// The median is the whole point: the subject's own pixels have ratios all over the place, but
    /// as long as they don't cover most of the frame they sit in the tails and the middle value
    /// still describes the background's shift. Falls back to 1.0 (no compensation) when too few
    /// pixels are bright enough to give a meaningful ratio — a nearly black reference photo cannot
    /// tell us anything about exposure.
    /// </summary>
    private static (double R, double G, double B) EstimateChannelGain(Image<Rgba32> frame, Image<Rgba32> reference)
    {
        // A ratio computed from near-black pixels is dominated by sensor noise, so ignore those.
        const int MinReferenceLevel = 16;
        const int MinSamples = 64;
        // Beyond ~2x the "drift" is not drift any more (lights switched off, lens covered); clamping
        // stops one pathological frame from rescaling the reference into nonsense.
        const double MinGain = 0.5;
        const double MaxGain = 2.0;

        var step = Math.Max(1, (int)Math.Sqrt(frame.Width * (long)frame.Height / 10_000.0));
        var ratiosR = new List<double>();
        var ratiosG = new List<double>();
        var ratiosB = new List<double>();

        frame.ProcessPixelRows(reference, (frameAccessor, refAccessor) =>
        {
            for (var y = 0; y < frame.Height; y += step)
            {
                var frameRow = frameAccessor.GetRowSpan(y);
                var refRow = refAccessor.GetRowSpan(y);
                for (var x = 0; x < frame.Width; x += step)
                {
                    var f = frameRow[x];
                    var r = refRow[x];
                    if (r.R >= MinReferenceLevel)
                    {
                        ratiosR.Add(f.R / (double)r.R);
                    }

                    if (r.G >= MinReferenceLevel)
                    {
                        ratiosG.Add(f.G / (double)r.G);
                    }

                    if (r.B >= MinReferenceLevel)
                    {
                        ratiosB.Add(f.B / (double)r.B);
                    }
                }
            }
        });

        return (Median(ratiosR), Median(ratiosG), Median(ratiosB));

        static double Median(List<double> values)
        {
            if (values.Count < MinSamples)
            {
                return 1.0;
            }

            values.Sort();
            var middle = values.Count / 2;
            var median = values.Count % 2 == 0
                ? (values[middle - 1] + values[middle]) / 2.0
                : values[middle];
            return Math.Clamp(median, MinGain, MaxGain);
        }
    }
}
