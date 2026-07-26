using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Camledian.Photobooth.Imaging;

/// <summary>
/// Common shape for "cut the subject out of frame" regardless of technique (green screen, AI,
/// hybrid — spec §10/§22/§26). Mutates the frame's alpha channel in place and returns the raw
/// foreground-probability mask so callers (e.g. HybridBackgroundRemovalProvider, diagnostics) can
/// inspect or recombine it.
/// </summary>
public interface IBackgroundRemovalService
{
    string Name { get; }

    /// <param name="highQuality">False for the fast live/AI preview path, true for the one-shot
    /// final render after capture (spec §12: ComposePreview is fast, ComposeFinal is not).</param>
    Task<float[]> ApplyAsync(Image<Rgba32> frame, bool highQuality, CancellationToken cancellationToken = default);
}
