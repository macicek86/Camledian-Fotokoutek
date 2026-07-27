using Camledian.Photobooth.Core.Models;
using Camledian.Photobooth.Imaging;
using Camledian.Photobooth.Imaging.ChromaKey;
using Camledian.Photobooth.Imaging.PixelMath;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Camledian.Photobooth.AI;

public sealed record MaskComparison(float[] ChromaMask, float[] AiMask, float[] CombinedMask, int Width, int Height);

/// <summary>
/// Combines chroma key (spec §10) and AI segmentation (spec §22) to improve exactly the cases each
/// one struggles with alone (spec §26): the chroma key gives crisp, high-resolution edges wherever
/// the background truly reads as the key color, while the AI mask is semantic and keeps thin hair
/// strands, fingers, and hands that have picked up green spill and would otherwise get cut as
/// background. Combination rule: per-pixel max of (chroma mask, AI mask slightly discounted), then a
/// shared edge-refinement blur pass so the seam between the two masks doesn't itself become a visible
/// edge.
/// </summary>
public class HybridBackgroundRemovalProvider(
    Func<ChromaKeySettings> getChromaSettings,
    AiBackgroundRemovalProvider aiProvider) : IBackgroundRemovalService
{
    private const float AiDiscount = 0.9f;
    private const double EdgeRefinementPixels = 1.5;

    public string Name => "Hybrid (Green Screen + AI)";

    public async Task<float[]> ApplyAsync(Image<Rgba32> frame, BackgroundRemovalOptions options, CancellationToken cancellationToken = default)
    {
        var comparison = await ComputeMasksAsync(frame, options, cancellationToken).ConfigureAwait(false);

        frame.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < frame.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < frame.Width; x++)
                {
                    var idx = (y * frame.Width) + x;
                    ref var px = ref row[x];
                    px.A = (byte)Math.Clamp((int)Math.Round(comparison.CombinedMask[idx] * 255.0), 0, 255);
                }
            }
        });

        return comparison.CombinedMask;
    }

    /// <summary>Exposes the individual chroma/AI masks alongside the combined result, for the admin
    /// "compare masks" diagnostic (spec §26: "Přidej testovací možnost srovnání masek").</summary>
    public async Task<MaskComparison> ComputeMasksAsync(Image<Rgba32> frame, BackgroundRemovalOptions options, CancellationToken cancellationToken = default)
    {
        using var aiFrame = frame.Clone();
        var aiMask = await aiProvider.ApplyAsync(aiFrame, options, cancellationToken).ConfigureAwait(false);

        // ChromaKeyProcessor mutates `frame` in place (alpha + green-spill despill on RGB) and hands
        // back its own raw mask — this is the copy of the frame the caller actually keeps.
        var chromaMask = ChromaKeyProcessor.Apply(frame, getChromaSettings());

        // See BackgroundSubtractionAiHybridProvider: an inference the model had no confidence in gets
        // no vote, leaving the (always available) chroma mask to decide on its own.
        var aiWeight = aiProvider.LastMaskWasLowConfidence ? 0f : AiDiscount;

        var combined = new float[chromaMask.Length];
        for (var i = 0; i < combined.Length; i++)
        {
            combined[i] = Math.Max(chromaMask[i], aiMask[i] * aiWeight);
        }

        BoxBlur.Apply(combined, frame.Width, frame.Height, EdgeRefinementPixels);

        return new MaskComparison(chromaMask, aiMask, combined, frame.Width, frame.Height);
    }
}
