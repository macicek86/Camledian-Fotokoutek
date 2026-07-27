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

    /// <param name="options">What this pass is for — see <see cref="BackgroundRemovalOptions"/>'s
    /// named presets rather than constructing one inline.</param>
    Task<float[]> ApplyAsync(Image<Rgba32> frame, BackgroundRemovalOptions options, CancellationToken cancellationToken = default);
}
