using Camledian.Photobooth.Imaging;
using Camledian.Photobooth.Imaging.BackgroundSubtraction;
using Camledian.Photobooth.Imaging.PixelMath;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Camledian.Photobooth.AI;

/// <summary>
/// Combines reference-photo background subtraction with AI segmentation — the same idea as
/// <see cref="HybridBackgroundRemovalProvider"/> (chroma key + AI), just for setups with no green
/// screen at all. Background subtraction gives crisp, precise edges wherever the scene actually
/// matches the captured reference photo; AI covers the case background subtraction struggles with
/// (the subject's clothing/skin happening to match the background color at that spot). Combination
/// rule: per-pixel max of (background-subtraction mask, AI mask slightly discounted), then a shared
/// edge-refinement blur so the seam between the two masks isn't itself a visible edge.
/// </summary>
public class BackgroundSubtractionAiHybridProvider(
    BackgroundSubtractionRemovalService backgroundSubtraction,
    AiBackgroundRemovalProvider aiProvider) : IBackgroundRemovalService
{
    private const float AiDiscount = 0.9f;
    private const double EdgeRefinementPixels = 1.5;

    public string Name => "Background Subtraction + AI";

    public async Task<float[]> ApplyAsync(Image<Rgba32> frame, BackgroundRemovalOptions options, CancellationToken cancellationToken = default)
    {
        using var aiFrame = frame.Clone();
        var aiMask = await aiProvider.ApplyAsync(aiFrame, options, cancellationToken).ConfigureAwait(false);

        // Mutates `frame` in place (alpha channel) and hands back its own raw mask — same convention
        // as ChromaKeyProcessor/BackgroundSubtractionProcessor, this is the copy the caller keeps.
        var subtractionMask = backgroundSubtraction.TryComputeMask(frame)
            ?? throw new InvalidOperationException("No background-subtraction reference photo captured yet.");

        // A mask the model had no confidence in is amplified noise; taking max() with it would smear
        // random blobs of background back into the photo. Dropping the AI's vote leaves the
        // subtraction mask in charge, which is the better of the two here anyway — it keeps whatever
        // wasn't in the reference photo, props included, without needing to recognise it.
        var aiWeight = aiProvider.LastMaskWasLowConfidence ? 0f : AiDiscount;

        var combined = new float[subtractionMask.Length];
        for (var i = 0; i < combined.Length; i++)
        {
            combined[i] = Math.Max(subtractionMask[i], aiMask[i] * aiWeight);
        }

        BoxBlur.Apply(combined, frame.Width, frame.Height, EdgeRefinementPixels);

        frame.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < frame.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < frame.Width; x++)
                {
                    var idx = (y * frame.Width) + x;
                    ref var px = ref row[x];
                    px.A = (byte)Math.Clamp((int)Math.Round(combined[idx] * 255.0), 0, 255);
                }
            }
        });

        return combined;
    }
}
