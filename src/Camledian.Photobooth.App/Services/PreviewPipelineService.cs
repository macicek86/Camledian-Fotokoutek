using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Camledian.Photobooth.App.Wpf;
using Camledian.Photobooth.Camera;
using Camledian.Photobooth.Core.Models;
using Camledian.Photobooth.Imaging;
using Camledian.Photobooth.Imaging.Composition;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Camledian.Photobooth.App.Services;

/// <summary>
/// The actual "kamera -&gt; frame -&gt; preview processing -&gt; UI" pipeline from spec §9. Runs entirely
/// off the UI thread; only the final (frozen, thread-safe) BitmapSource handoff touches the
/// Dispatcher. Backed by ICameraProvider.PreviewFrames, a LatestFrameBox, so a slow processing step
/// never causes a backlog — the loop always picks up the newest camera frame available.
/// </summary>
public sealed class PreviewPipelineService(
    ICameraProvider camera,
    BackgroundRemovalServiceFactory backgroundRemovalFactory,
    ImageCompositionService compositionService,
    Dispatcher dispatcher,
    ILogger<PreviewPipelineService> logger) : IAsyncDisposable
{
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public event EventHandler<BitmapSource>? FrameReady;

    public Image<Rgba32>? SelectedBackground { get; set; }
    public Image<Rgba32>? SelectedOverlay { get; set; }
    public OutputTemplate Template { get; set; } = OutputTemplate.DigitalLandscape;

    public double LastChromaKeyOrAiMs { get; private set; }
    public double LastCompositionMs { get; private set; }

    public async Task StartAsync(CameraStartOptions options, CancellationToken cancellationToken = default)
    {
        await camera.StartAsync(options, cancellationToken).ConfigureAwait(false);
        _cts = new CancellationTokenSource();
        _loopTask = Task.Run(() => RunLoopAsync(_cts.Token), CancellationToken.None);
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        if (_loopTask is not null)
        {
            try
            {
                await _loopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        await camera.StopAsync().ConfigureAwait(false);
    }

    private async Task RunLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            CameraFrame? cameraFrame;
            try
            {
                cameraFrame = await camera.PreviewFrames.WaitNextAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (cameraFrame is null)
            {
                continue;
            }

            using (cameraFrame)
            {
                try
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var backgroundRemoval = backgroundRemovalFactory.Resolve();
                    await backgroundRemoval.ApplyAsync(cameraFrame.Image, highQuality: false, token).ConfigureAwait(false);
                    LastChromaKeyOrAiMs = sw.Elapsed.TotalMilliseconds;

                    sw.Restart();
                    using var background = SelectedBackground?.Clone() ?? CreateBlankCanvas();
                    using var composed = compositionService.ComposePreview(background, cameraFrame.Image, SelectedOverlay, Template);
                    LastCompositionMs = sw.Elapsed.TotalMilliseconds;

                    var bitmap = ImageSharpWpfInterop.ToBitmapSource(composed);
                    _ = dispatcher.BeginInvoke(() => FrameReady?.Invoke(this, bitmap));
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Preview pipeline frame failed.");
                }
            }
        }
    }

    private Image<Rgba32> CreateBlankCanvas()
    {
        var image = new Image<Rgba32>(Template.WidthPx, Template.HeightPx);
        image.Mutate(ctx => ctx.Fill(SixLabors.ImageSharp.Color.FromRgb(30, 30, 34)));
        return image;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        await camera.DisposeAsync().ConfigureAwait(false);
    }
}
