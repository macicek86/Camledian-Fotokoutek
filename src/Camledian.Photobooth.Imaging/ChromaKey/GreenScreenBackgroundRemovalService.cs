using Camledian.Photobooth.Core.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Camledian.Photobooth.Imaging.ChromaKey;

/// <summary>Adapts <see cref="ChromaKeyProcessor"/> to <see cref="IBackgroundRemovalService"/>. Takes
/// a settings accessor (not a snapshot) so admin edits to hue/tolerance/feather apply live.</summary>
public class GreenScreenBackgroundRemovalService(Func<ChromaKeySettings> getSettings) : IBackgroundRemovalService
{
    public string Name => "Green Screen";

    public Task<float[]> ApplyAsync(Image<Rgba32> frame, bool highQuality, CancellationToken cancellationToken = default)
    {
        var mask = ChromaKeyProcessor.Apply(frame, getSettings());
        return Task.FromResult(mask);
    }
}
