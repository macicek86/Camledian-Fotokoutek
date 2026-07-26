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
                        var dr = f.R - r.R;
                        var dg = f.G - r.G;
                        var db = f.B - r.B;
                        var distance = Math.Sqrt((dr * dr) + (dg * dg) + (db * db));
                        var normalized = distance / maxDistance;

                        var idx = (y * width) + x;
                        mask[idx] = normalized > thresholdFraction ? 1f : 0f;
                    }
                }
            });

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
}
