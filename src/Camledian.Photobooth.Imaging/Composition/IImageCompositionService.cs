using Camledian.Photobooth.Core.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Camledian.Photobooth.Imaging.Composition;

public interface IImageCompositionService
{
    /// <summary>Fast composition for the live/AI preview loop. Lower-quality resampling, expected to
    /// run many times per second.</summary>
    Image<Rgba32> ComposePreview(Image<Rgba32> background, Image<Rgba32> foreground, Image<Rgba32>? overlay, OutputTemplate template);

    /// <summary>High-quality composition run once after capture. Uses a sharper resampler; not
    /// required to be fast.</summary>
    Image<Rgba32> ComposeFinal(Image<Rgba32> background, Image<Rgba32> foreground, Image<Rgba32>? overlay, OutputTemplate template);
}
