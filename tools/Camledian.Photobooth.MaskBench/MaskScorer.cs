using SixLabors.ImageSharp;

namespace Camledian.Photobooth.MaskBench;

/// <param name="Iou">Overlap between what was kept and what should have been kept. One number for
/// "how right was this overall".</param>
/// <param name="ForegroundLost">Share of the subject and props that got removed — the guest with a
/// missing hand.</param>
/// <param name="BackgroundKept">Share of the background that stayed — the room showing through the
/// replacement background.</param>
/// <param name="PropRetention">Per prop, how much of it survived. Averages hide this: a prop is a
/// small part of the frame, and losing all of it costs almost nothing in IoU while ruining the photo.</param>
public readonly record struct MaskScore(
    double Iou,
    double ForegroundLost,
    double BackgroundKept,
    IReadOnlyList<(string Name, double Kept)> PropRetention);

public static class MaskScorer
{
    /// <param name="mask">Alpha per pixel, as the removal service produced it.</param>
    public static MaskScore Score(float[] mask, Scene scene)
    {
        long intersection = 0, union = 0, foreground = 0, foregroundKept = 0, background = 0, backgroundKept = 0;

        for (var i = 0; i < scene.Truth.Length; i++)
        {
            var kept = mask[i] > 0.5f;
            var shouldKeep = scene.Truth[i];

            if (shouldKeep)
            {
                foreground++;
                if (kept)
                {
                    foregroundKept++;
                }
            }
            else
            {
                background++;
                if (kept)
                {
                    backgroundKept++;
                }
            }

            if (kept && shouldKeep)
            {
                intersection++;
            }

            if (kept || shouldKeep)
            {
                union++;
            }
        }

        var props = new List<(string, double)>();
        foreach (var (name, area) in scene.Props)
        {
            double sum = 0;
            var count = 0;
            for (var y = area.Top; y < Math.Min(area.Bottom, scene.Height); y++)
            {
                for (var x = area.Left; x < Math.Min(area.Right, scene.Width); x++)
                {
                    sum += mask[(y * scene.Width) + x];
                    count++;
                }
            }

            props.Add((name, count == 0 ? 0 : sum / count));
        }

        return new MaskScore(
            union == 0 ? 0 : intersection / (double)union,
            foreground == 0 ? 0 : 1 - (foregroundKept / (double)foreground),
            background == 0 ? 0 : backgroundKept / (double)background,
            props);
    }
}
