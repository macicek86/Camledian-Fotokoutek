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
    /// <param name="halfResolution">Compare at half resolution — see
    /// <see cref="BackgroundSubtractionSettings.HalfResolutionPreview"/>. Callers rendering the photo
    /// that gets saved must leave this false.</param>
    public static float[] Apply(
        Image<Rgba32> frame,
        Image<Rgba32> reference,
        BackgroundSubtractionSettings settings,
        bool halfResolution = false)
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

        // Halving the resolution averages 2x2 pixels together, which halves the sensor noise the
        // comparison has to tolerate — and costs a quarter of the work.
        var scale = halfResolution ? 0.5 : 1.0;
        var workWidth = Math.Max(1, (int)Math.Round(width * scale));
        var workHeight = Math.Max(1, (int)Math.Round(height * scale));

        Image<Rgba32>? workFrame = null;
        Image<Rgba32>? workReference = null;

        try
        {
            var comparisonFrame = frame;
            var comparisonReference = referenceToUse;
            if (workWidth != width || workHeight != height)
            {
                // Box resampling is the averaging we're after — a sharper kernel would preserve the
                // noise we are trying to get rid of.
                workFrame = frame.Clone(ctx => ctx.Resize(workWidth, workHeight, KnownResamplers.Box));
                workReference = referenceToUse.Clone(ctx => ctx.Resize(workWidth, workHeight, KnownResamplers.Box));
                comparisonFrame = workFrame;
                comparisonReference = workReference;
            }

            var mask = new float[workWidth * workHeight];
            var maxDistance = Math.Sqrt(3 * 255.0 * 255.0);
            var thresholdFraction = Math.Clamp(settings.ThresholdDistance / maxDistance, 0.001, 1.0);

            var gain = settings.CompensateLightingDrift
                ? EstimateChannelGain(comparisonFrame, comparisonReference)
                : (R: 1.0, G: 1.0, B: 1.0);

            comparisonFrame.ProcessPixelRows(comparisonReference, (frameAccessor, refAccessor) =>
            {
                for (var y = 0; y < workHeight; y++)
                {
                    var frameRow = frameAccessor.GetRowSpan(y);
                    var refRow = refAccessor.GetRowSpan(y);
                    for (var x = 0; x < workWidth; x++)
                    {
                        var f = frameRow[x];
                        var r = refRow[x];
                        var dr = f.R - (r.R * gain.R);
                        var dg = f.G - (r.G * gain.G);
                        var db = f.B - (r.B * gain.B);
                        var distance = Math.Sqrt((dr * dr) + (dg * dg) + (db * db));
                        var normalized = distance / maxDistance;

                        var idx = (y * workWidth) + x;
                        mask[idx] = normalized > thresholdFraction ? 1f : 0f;
                    }
                }
            });

            // Close the pinholes first, then feather: blurring a hole-ridden mask only turns crisp
            // holes into soft grey ones. Both radii are in pixels of the *final* image, so they scale
            // with the working resolution.
            Morphology.Close(mask, workWidth, workHeight, settings.FillHolesPixels * scale);
            BoxBlur.Apply(mask, workWidth, workHeight, settings.FeatherPixels * scale);

            if (workWidth != width || workHeight != height)
            {
                mask = MaskResize.Bilinear(mask, workWidth, workHeight, width, height);
            }

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
            workFrame?.Dispose();
            workReference?.Dispose();
            resizedReference?.Dispose();
        }
    }

    /// <summary>
    /// Per-channel factor that maps the reference photo onto the current frame's exposure/white
    /// balance.
    ///
    /// The estimate has to answer "how did the *background* change" while most of what it can see may
    /// well be a guest standing right up against the lens. Two ideas do the work:
    ///
    /// 1. Background pixels all moved by the same factor, so they form tight clusters in the ratio
    ///    histogram, while the subject's own colours scatter. Cluster peaks are therefore the
    ///    estimate — never the median, which starts describing the subject the moment they cover
    ///    half the frame (measured: 35 % of the background then wrongly kept).
    /// 2. Nothing is compensated unless that peak holds a tenth of the sampled pixels. Below that
    ///    there is no coherent "unchanged part of the scene" to measure against, and doing nothing is
    ///    a known, mild failure where guessing is not.
    /// </summary>
    private static (double R, double G, double B) EstimateChannelGain(Image<Rgba32> frame, Image<Rgba32> reference)
    {
        // A ratio computed from near-black pixels is dominated by sensor noise, so ignore those.
        const int MinReferenceLevel = 16;
        const int MinSamples = 256;
        // Beyond ~2x the "drift" is not drift any more (lights switched off, lens covered); clamping
        // stops one pathological frame from rescaling the reference into nonsense.
        const double MinGain = 0.5;
        const double MaxGain = 2.0;
        const double BinWidth = 0.01;
        // The winning cluster has to be a real population, not a handful of pixels that agreed.
        const double MinPeakShare = 0.10;
        // How far the three channel gains may disagree and still be lighting drift rather than a
        // flat-coloured object. Exposure moves them together (spread 1.0); a strong warm/cool shift
        // is maybe 1.15.
        const double MaxChannelSpread = 1.25;

        var step = Math.Max(1, (int)Math.Sqrt(frame.Width * (long)frame.Height / 10_000.0));
        var samples = new List<Sample>();

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
                    if (r.R < MinReferenceLevel || r.G < MinReferenceLevel || r.B < MinReferenceLevel)
                    {
                        continue;
                    }

                    // Cluster on one number (luma) so all three channels are estimated from the same
                    // set of pixels — otherwise a colour cast could pick a different "background" per
                    // channel and tint the comparison.
                    var refLuma = (0.299 * r.R) + (0.587 * r.G) + (0.114 * r.B);
                    var frameLuma = (0.299 * f.R) + (0.587 * f.G) + (0.114 * f.B);
                    samples.Add(new Sample(
                        frameLuma / refLuma,
                        f.R / (double)r.R,
                        f.G / (double)r.G,
                        f.B / (double)r.B));
                }
            }
        });

        if (samples.Count < MinSamples)
        {
            return (1.0, 1.0, 1.0);
        }

        // Histogram the luma ratios and take the fullest bin (plus its neighbours, so a cluster
        // straddling a bin boundary isn't split in half).
        var bins = new Dictionary<int, int>();
        foreach (var sample in samples)
        {
            if (sample.Luma is >= MinGain and <= MaxGain)
            {
                var bin = (int)(sample.Luma / BinWidth);
                bins[bin] = bins.GetValueOrDefault(bin) + 1;
            }
        }

        if (bins.Count == 0)
        {
            return (1.0, 1.0, 1.0);
        }

        var peakBin = int.MinValue;
        var peakCount = 0;
        foreach (var (bin, _) in bins)
        {
            var count = bins.GetValueOrDefault(bin - 1) + bins[bin] + bins.GetValueOrDefault(bin + 1);
            if (count > peakCount)
            {
                peakCount = count;
                peakBin = bin;
            }
        }

        if (peakBin == int.MinValue || peakCount < samples.Count * MinPeakShare)
        {
            return (1.0, 1.0, 1.0);
        }

        if (AverageOverCluster(samples, peakBin, BinWidth) is not { } gain)
        {
            return (1.0, 1.0, 1.0);
        }

        // Sanity check on what was found: a camera re-exposing moves all three channels by nearly the
        // same factor, and a white-balance shift tilts them only mildly. A cluster whose channels
        // disagree wildly is not lighting drift — it is a large flat surface that happens to be one
        // colour, i.e. the guest's plain T-shirt filling the frame. Scaling the reference by *that*
        // would map it onto the shirt, turning the guest into background and the room into foreground.
        var spread = Math.Max(gain.R, Math.Max(gain.G, gain.B)) / Math.Max(1e-6, Math.Min(gain.R, Math.Min(gain.G, gain.B)));
        if (spread > MaxChannelSpread)
        {
            return (1.0, 1.0, 1.0);
        }

        return (Math.Clamp(gain.R, MinGain, MaxGain),
                Math.Clamp(gain.G, MinGain, MaxGain),
                Math.Clamp(gain.B, MinGain, MaxGain));
    }

    /// <summary>One sampled pixel's frame/reference ratio: the luma ratio the clustering runs on,
    /// plus the per-channel ratios averaged once a cluster has been picked.</summary>
    private readonly record struct Sample(double Luma, double R, double G, double B);

    /// <summary>Mean per-channel ratio over the samples inside one histogram peak (the peak bin plus
    /// a bin either side). Null when the peak turned out to be empty after the widening.</summary>
    private static (double R, double G, double B)? AverageOverCluster(List<Sample> samples, int peakBin, double binWidth)
    {
        var low = (peakBin - 1) * binWidth;
        var high = (peakBin + 2) * binWidth;

        double sumR = 0, sumG = 0, sumB = 0;
        var used = 0;
        foreach (var sample in samples)
        {
            if (sample.Luma >= low && sample.Luma < high)
            {
                sumR += sample.R;
                sumG += sample.G;
                sumB += sample.B;
                used++;
            }
        }

        return used == 0 ? null : (sumR / used, sumG / used, sumB / used);
    }
}
