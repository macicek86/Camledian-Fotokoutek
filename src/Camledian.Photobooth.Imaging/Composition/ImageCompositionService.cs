using Camledian.Photobooth.Core.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Transforms;

namespace Camledian.Photobooth.Imaging.Composition;

/// <summary>
/// Layers Background -&gt; Foreground (person, already alpha-keyed) -&gt; Overlay onto an
/// <see cref="OutputTemplate"/> canvas, respecting each layer's alpha channel. Preview and Final use
/// the same layering logic with different resamplers, per spec §12.
/// </summary>
public class ImageCompositionService : IImageCompositionService
{
    public Image<Rgba32> ComposePreview(Image<Rgba32> background, Image<Rgba32> foreground, Image<Rgba32>? overlay, OutputTemplate template)
        => Compose(background, foreground, overlay, template, KnownResamplers.Triangle);

    public Image<Rgba32> ComposeFinal(Image<Rgba32> background, Image<Rgba32> foreground, Image<Rgba32>? overlay, OutputTemplate template)
        => Compose(background, foreground, overlay, template, KnownResamplers.Lanczos3);

    private static Image<Rgba32> Compose(
        Image<Rgba32> background,
        Image<Rgba32> foreground,
        Image<Rgba32>? overlay,
        OutputTemplate template,
        IResampler resampler)
    {
        var canvas = new Image<Rgba32>(template.WidthPx, template.HeightPx);
        DrawLayer(canvas, background, template.BackgroundPlacement, resampler, cover: true);
        DrawLayer(canvas, foreground, template.ForegroundPlacement, resampler, cover: true);
        if (overlay is not null)
        {
            DrawLayer(canvas, overlay, template.OverlayPlacement, resampler, cover: false);
        }

        return canvas;
    }

    private static void DrawLayer(Image<Rgba32> canvas, Image<Rgba32> source, PlacementRect rect, IResampler resampler, bool cover)
    {
        var destX = (int)Math.Round(rect.X * canvas.Width);
        var destY = (int)Math.Round(rect.Y * canvas.Height);
        var destW = (int)Math.Round(rect.Width * canvas.Width);
        var destH = (int)Math.Round(rect.Height * canvas.Height);
        if (destW <= 0 || destH <= 0)
        {
            return;
        }

        using var resized = source.Clone(ctx => ctx.Resize(new ResizeOptions
        {
            Size = new Size(destW, destH),
            Mode = cover ? ResizeMode.Crop : ResizeMode.Stretch,
            Sampler = resampler,
        }));

        canvas.Mutate(ctx => ctx.DrawImage(resized, new Point(destX, destY), 1f));
    }
}
