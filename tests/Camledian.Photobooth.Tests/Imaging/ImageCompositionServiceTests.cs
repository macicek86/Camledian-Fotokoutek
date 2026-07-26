using Camledian.Photobooth.Core.Models;
using Camledian.Photobooth.Imaging.Composition;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Camledian.Photobooth.Tests.Imaging;

public class ImageCompositionServiceTests
{
    private static Image<Rgba32> Solid(int w, int h, Rgba32 color)
    {
        var image = new Image<Rgba32>(w, h);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < h; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < w; x++)
                {
                    row[x] = color;
                }
            }
        });
        return image;
    }

    private static OutputTemplate TestTemplate(int width, int height) => new()
    {
        Id = "test",
        Name = "Test",
        WidthPx = width,
        HeightPx = height,
    };

    [Fact]
    public void ComposeFinalOutputMatchesTemplateDimensions()
    {
        var service = new ImageCompositionService();
        using var background = Solid(200, 100, new Rgba32(0, 0, 255));
        using var foreground = Solid(50, 50, new Rgba32(255, 0, 0, 255));

        using var result = service.ComposeFinal(background, foreground, null, TestTemplate(640, 480));

        Assert.Equal(640, result.Width);
        Assert.Equal(480, result.Height);
    }

    [Fact]
    public void TransparentForegroundLetsBackgroundShowThrough()
    {
        var service = new ImageCompositionService();
        var backgroundColor = new Rgba32(10, 20, 200);
        using var background = Solid(100, 100, backgroundColor);
        using var foreground = Solid(100, 100, new Rgba32(255, 0, 0, 0)); // fully transparent

        using var result = service.ComposeFinal(background, foreground, null, TestTemplate(100, 100));

        var pixel = result[50, 50];
        Assert.Equal(backgroundColor.R, pixel.R);
        Assert.Equal(backgroundColor.G, pixel.G);
        Assert.Equal(backgroundColor.B, pixel.B);
    }

    [Fact]
    public void OpaqueForegroundHidesBackground()
    {
        var service = new ImageCompositionService();
        using var background = Solid(100, 100, new Rgba32(10, 20, 200));
        var foregroundColor = new Rgba32(255, 0, 0, 255);
        using var foreground = Solid(100, 100, foregroundColor);

        using var result = service.ComposeFinal(background, foreground, null, TestTemplate(100, 100));

        var pixel = result[50, 50];
        Assert.Equal(foregroundColor.R, pixel.R);
        Assert.Equal(foregroundColor.G, pixel.G);
        Assert.Equal(foregroundColor.B, pixel.B);
    }

    [Fact]
    public void OverlayIsDrawnOnTopOfForegroundAndBackground()
    {
        var service = new ImageCompositionService();
        using var background = Solid(100, 100, new Rgba32(0, 0, 0, 255));
        using var foreground = Solid(100, 100, new Rgba32(0, 0, 0, 0));
        var overlayColor = new Rgba32(255, 255, 0, 255);
        using var overlay = Solid(100, 100, overlayColor);

        using var result = service.ComposeFinal(background, foreground, overlay, TestTemplate(100, 100));

        var pixel = result[50, 50];
        Assert.Equal(overlayColor.R, pixel.R);
        Assert.Equal(overlayColor.G, pixel.G);
        Assert.Equal(overlayColor.B, pixel.B);
    }

    [Fact]
    public void ComposePreviewAlsoRespectsTemplateSize()
    {
        var service = new ImageCompositionService();
        using var background = Solid(50, 50, new Rgba32(1, 2, 3));
        using var foreground = Solid(50, 50, new Rgba32(4, 5, 6, 0));

        using var result = service.ComposePreview(background, foreground, null, TestTemplate(320, 180));

        Assert.Equal(320, result.Width);
        Assert.Equal(180, result.Height);
    }
}
