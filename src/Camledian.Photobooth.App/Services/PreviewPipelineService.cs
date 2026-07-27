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

    /// <summary>Set when the active technique ran fine but cut out (almost) nothing — every pixel
    /// stayed opaque, so the composed frame is the raw camera image and the chosen background is
    /// completely hidden behind it. Without this the kiosk looked identical to "background
    /// replacement is broken": a correctly configured Green Screen mode pointed at a room with no
    /// green screen produces exactly that picture, and nothing on screen said why.
    /// <see cref="BackgroundRemovalServiceFactory.LastFallbackNotice"/> only covers the different
    /// case where a prerequisite was missing and a *substitute* technique was used.</summary>
    public string? EmptyMaskNotice { get; private set; }

    /// <summary>
    /// Skips the whole keying+composition pass while no screen is showing the live preview. It runs
    /// for most of an event otherwise — the kiosk sits on Idle between guests — burning a full AI
    /// inference several times a second on frames that are never displayed, and competing for CPU
    /// with the burst thumbnails and the final render, which is exactly the work a guest is waiting
    /// on. Paused by default because the kiosk starts on Idle.
    ///
    /// Deliberately does not touch the camera: it stays running so the preview resumes instantly,
    /// and a paused loop stops draining <see cref="ICameraProvider.PreviewFrames"/>, which is a
    /// single-slot mailbox that <c>CaptureStillAsync</c> draws from too.
    /// </summary>
    public bool IsPaused { get; set; } = true;

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
            if (IsPaused)
            {
                try
                {
                    await Task.Delay(50, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                continue;
            }

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
                    var mask = await backgroundRemoval.ApplyAsync(cameraFrame.Image, BackgroundRemovalOptions.LivePreview, token).ConfigureAwait(false);
                    LastChromaKeyOrAiMs = sw.Elapsed.TotalMilliseconds;
                    UpdateEmptyMaskNotice(mask, backgroundRemoval.Name);

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

    /// <summary>Measures how much of the frame the mask actually keyed out and raises/clears
    /// <see cref="EmptyMaskNotice"/>. Subsampled (every 16th pixel) because this runs per frame on a
    /// full-resolution mask, and hysteresis'd — raise below 1% removed, clear above 3% — so a subject
    /// stepping right up to the lens can't make the warning strobe on and off.</summary>
    private void UpdateEmptyMaskNotice(float[] mask, string techniqueName)
    {
        const int step = 16;
        var sampled = 0;
        var removed = 0;
        for (var i = 0; i < mask.Length; i += step)
        {
            sampled++;
            if (mask[i] < 0.5f)
            {
                removed++;
            }
        }

        if (sampled == 0)
        {
            return;
        }

        var removedFraction = removed / (double)sampled;
        if (removedFraction < 0.01)
        {
            EmptyMaskNotice = $"{techniqueName}: z obrazu se neodstranilo nic — vybrané pozadí je proto celé schované. " +
                "Zkontrolujte green screen a nasvícení, nebo zvolte jiný režim v Admin > AI / Hybrid.";
        }
        else if (removedFraction > 0.03)
        {
            EmptyMaskNotice = null;
        }
    }

    /// <summary>
    /// Renders one already-captured frame the same way the live loop renders camera frames — keyed,
    /// then composited onto the selected background — scaled down for a thumbnail. The burst picker
    /// needs this: it used to show the raw camera stills, so the guest chose between pictures that
    /// looked nothing like the photo they would actually get.
    ///
    /// <paramref name="frame"/> is never mutated. The chosen shot is re-keyed from the untouched
    /// original at full quality by <see cref="PhotoCaptureService.ProcessCapturedAsync"/>.
    /// </summary>
    public async Task<Image<Rgba32>> RenderStillCompositeAsync(
        Image<Rgba32> frame,
        int width,
        CancellationToken token = default)
    {
        var template = Template;
        var height = Math.Max(1, (int)Math.Round(width * (double)template.HeightPx / template.WidthPx));
        var thumbnailTemplate = new OutputTemplate
        {
            Id = template.Id,
            Name = template.Name,
            WidthPx = width,
            HeightPx = height,
            BackgroundPlacement = template.BackgroundPlacement,
            ForegroundPlacement = template.ForegroundPlacement,
            OverlayPlacement = template.OverlayPlacement,
        };

        using var working = frame.Clone();
        var backgroundRemoval = backgroundRemovalFactory.Resolve();

        // StillPreview, not FinalRender: every burst shot needs a mask computed from *that* shot
        // (otherwise back-to-back calls land inside the AI throttle window and reuse whichever mask
        // the live loop last produced, for a different frame entirely) — but at a few hundred pixels
        // wide there is nothing to gain from the heavy final model.
        await backgroundRemoval.ApplyAsync(working, BackgroundRemovalOptions.StillPreview, token).ConfigureAwait(false);

        using var background = SelectedBackground?.Clone() ?? CreateBlankCanvas(thumbnailTemplate);
        return compositionService.ComposePreview(background, working, SelectedOverlay, thumbnailTemplate);
    }

    private Image<Rgba32> CreateBlankCanvas() => CreateBlankCanvas(Template);

    private static Image<Rgba32> CreateBlankCanvas(OutputTemplate template)
    {
        var image = new Image<Rgba32>(template.WidthPx, template.HeightPx);
        image.Mutate(ctx => ctx.Fill(SixLabors.ImageSharp.Color.FromRgb(30, 30, 34)));
        return image;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        await camera.DisposeAsync().ConfigureAwait(false);
    }
}
