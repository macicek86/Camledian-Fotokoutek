using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Camledian.Photobooth.AI;

/// <summary>Writes each mask in a <see cref="MaskComparison"/> out as a grayscale PNG so an admin can
/// visually compare chroma/AI/combined results side by side (spec §26).</summary>
public static class MaskComparisonExporter
{
    public static async Task<(string ChromaPath, string AiPath, string CombinedPath)> SaveAsync(
        MaskComparison comparison, string outputDirectory, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);

        var chromaPath = Path.Combine(outputDirectory, "mask-chroma.png");
        var aiPath = Path.Combine(outputDirectory, "mask-ai.png");
        var combinedPath = Path.Combine(outputDirectory, "mask-combined.png");

        await SaveMaskAsync(comparison.ChromaMask, comparison.Width, comparison.Height, chromaPath, cancellationToken).ConfigureAwait(false);
        await SaveMaskAsync(comparison.AiMask, comparison.Width, comparison.Height, aiPath, cancellationToken).ConfigureAwait(false);
        await SaveMaskAsync(comparison.CombinedMask, comparison.Width, comparison.Height, combinedPath, cancellationToken).ConfigureAwait(false);

        return (chromaPath, aiPath, combinedPath);
    }

    private static async Task SaveMaskAsync(float[] mask, int width, int height, string path, CancellationToken cancellationToken)
    {
        using var image = new Image<L8>(width, height);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < width; x++)
                {
                    var value = (byte)Math.Clamp((int)Math.Round(mask[(y * width) + x] * 255.0), 0, 255);
                    row[x] = new L8(value);
                }
            }
        });

        await image.SaveAsPngAsync(path, cancellationToken).ConfigureAwait(false);
    }
}
