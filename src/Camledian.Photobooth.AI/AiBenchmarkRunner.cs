using System.Diagnostics;
using Camledian.Photobooth.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Camledian.Photobooth.AI;

public sealed record AiBenchmarkResult(int Iterations, double MinMs, double MaxMs, double AvgMs);

/// <summary>Benchmarks any <see cref="IBackgroundRemovalService"/> (green screen, AI, hybrid) against
/// a synthetic frame and logs per-run timing — spec §23: "Přidej benchmark. Loguj inference time."
/// Wired up behind the Diagnostics screen's "Test AI" button.</summary>
public static class AiBenchmarkRunner
{
    public static async Task<AiBenchmarkResult> RunAsync(
        IBackgroundRemovalService provider,
        int width = 1280,
        int height = 720,
        int iterations = 10,
        CancellationToken cancellationToken = default)
    {
        var timings = new List<double>(iterations);

        for (var i = 0; i < iterations; i++)
        {
            using var frame = CreateSyntheticFrame(width, height, i);
            var sw = Stopwatch.StartNew();
            // ForceFreshMask (via StillPreview) matters here: with the plain live-preview options
            // these same-resolution frames land inside the AI throttle window, so every iteration
            // after the first would time a cached-mask reuse instead of an actual inference and the
            // reported min/avg would be far too optimistic.
            await provider.ApplyAsync(frame, BackgroundRemovalOptions.StillPreview, cancellationToken).ConfigureAwait(false);
            sw.Stop();
            timings.Add(sw.Elapsed.TotalMilliseconds);
        }

        return new AiBenchmarkResult(iterations, timings.Min(), timings.Max(), timings.Average());
    }

    private static Image<Rgba32> CreateSyntheticFrame(int width, int height, int seed)
    {
        var image = new Image<Rgba32>(width, height);
        var rnd = new Random(seed);
        image.Mutate(ctx => ctx.Fill(Color.FromRgb((byte)rnd.Next(0, 255), (byte)rnd.Next(0, 255), (byte)rnd.Next(0, 255))));
        return image;
    }
}
