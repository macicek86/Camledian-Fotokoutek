using Camledian.Photobooth.Core.Models;
using Camledian.Photobooth.Imaging.PixelMath;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Camledian.Photobooth.Imaging.ChromaKey;

/// <summary>
/// Real HSV chroma key, not a "G &gt; X" threshold: hue/saturation/value scoring, a soft feather band
/// (box blur + smoothstep) instead of a hard cutout, and green/blue-spill suppression on the
/// surviving foreground edge pixels. Mutates the frame's alpha channel in place.
/// </summary>
public static class ChromaKeyProcessor
{
    /// <summary>Applies the key and writes the resulting alpha into <paramref name="frame"/>.
    /// Returns the raw foreground-probability mask (0=background..1=foreground) for callers that
    /// want to combine it with another mask (see HybridBackgroundRemovalProvider).</summary>
    public static float[] Apply(Image<Rgba32> frame, ChromaKeySettings settings)
    {
        var width = frame.Width;
        var height = frame.Height;
        var mask = new float[width * height];
        var hueScore = new float[width * height];

        frame.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < width; x++)
                {
                    var px = row[x];
                    var (h, s, v) = ColorMath.RgbToHsv(px.R, px.G, px.B);
                    var hueDist = ColorMath.HueDistance(h, settings.TargetHueDegrees);
                    var hueCloseness = ColorMath.Clamp01(1 - (hueDist / Math.Max(1e-6, settings.HueToleranceDegrees)));
                    var satScore = s >= settings.SaturationThreshold
                        ? 1.0
                        : settings.SaturationThreshold <= 1e-6 ? 0.0 : s / settings.SaturationThreshold;
                    var valScore = v >= settings.ValueThreshold
                        ? 1.0
                        : settings.ValueThreshold <= 1e-6 ? 0.0 : v / settings.ValueThreshold;
                    var keyness = hueCloseness * satScore * valScore;

                    var idx = (y * width) + x;
                    mask[idx] = keyness > 0.5 ? 0f : 1f; // pre-blur binary foreground probability
                    hueScore[idx] = (float)hueCloseness;
                }
            }
        });

        BoxBlur.Apply(mask, width, height, settings.FeatherPixels);
        BoxBlur.Apply(mask, width, height, settings.EdgeSmoothingPixels);

        var dominantChannel = DominantChannelFor(settings.TargetHueDegrees);

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
                    if (alpha > 0.02 && hueScore[idx] > 0.15 && settings.SpillSuppressionStrength > 0)
                    {
                        DespillPixel(ref px, dominantChannel, hueScore[idx] * settings.SpillSuppressionStrength);
                    }

                    px.A = (byte)Math.Clamp((int)Math.Round(alpha * 255.0), 0, 255);
                }
            }
        });

        return mask;
    }

    private static void DespillPixel(ref Rgba32 px, int dominantChannel, double strength)
    {
        strength = ColorMath.Clamp01(strength);
        switch (dominantChannel)
        {
            case 1: // green screen
                var avgRb = (px.R + px.B) / 2.0;
                if (px.G > avgRb)
                {
                    px.G = (byte)Math.Round(px.G + ((avgRb - px.G) * strength));
                }

                break;
            case 2: // blue screen
                var avgRg = (px.R + px.G) / 2.0;
                if (px.B > avgRg)
                {
                    px.B = (byte)Math.Round(px.B + ((avgRg - px.B) * strength));
                }

                break;
            default: // red screen (uncommon, kept for hue-agnostic completeness)
                var avgGb = (px.G + px.B) / 2.0;
                if (px.R > avgGb)
                {
                    px.R = (byte)Math.Round(px.R + ((avgGb - px.R) * strength));
                }

                break;
        }
    }

    private static int DominantChannelFor(double targetHueDegrees) => targetHueDegrees switch
    {
        >= 60 and < 180 => 1, // green
        >= 180 and < 300 => 2, // blue
        _ => 0, // red
    };
}
