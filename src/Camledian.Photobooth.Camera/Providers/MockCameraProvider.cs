using System.Diagnostics;
using Camledian.Photobooth.Core.Utilities;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Camledian.Photobooth.Camera.Providers;

/// <summary>
/// Hardware-free stand-in for a real webcam (spec §7/§8). Generates a synthetic green-screen scene
/// with a moving skin-tone "person" ellipse so the rest of the pipeline (chroma key, composition,
/// AI, capture, storage) can be developed and tested without physical camera access — which is
/// exactly the situation in this Linux devcontainer.
/// </summary>
public sealed class MockCameraProvider : ICameraProvider
{
    private static readonly Color GreenScreenColor = Color.FromRgb(20, 200, 40);
    private static readonly Color SkinTone = Color.FromRgb(222, 184, 155);

    private readonly LatestFrameBox<CameraFrame> _previewFrames = new();
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private long _frameNumber;
    private readonly Stopwatch _clock = new();

    public string Name => "Mock Camera";
    public bool IsRunning { get; private set; }
    public int Width { get; private set; } = 1280;
    public int Height { get; private set; } = 720;
    public double ActualFps { get; private set; }
    public LatestFrameBox<CameraFrame> PreviewFrames => _previewFrames;

    public IReadOnlyList<CameraDeviceInfo> ListDevices() => [new CameraDeviceInfo("mock", "Mock Camera", 0)];

    public Task StartAsync(CameraStartOptions options, CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            return Task.CompletedTask;
        }

        Width = options.Width > 0 ? options.Width : 1280;
        Height = options.Height > 0 ? options.Height : 720;
        var fps = options.Fps > 0 ? options.Fps : 30;

        _loopCts = new CancellationTokenSource();
        var token = _loopCts.Token;
        _clock.Restart();
        IsRunning = true;
        _loopTask = Task.Run(() => RunLoopAsync(fps, token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (!IsRunning)
        {
            return;
        }

        IsRunning = false;
        _loopCts?.Cancel();
        if (_loopTask is not null)
        {
            try
            {
                await _loopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // expected on cancellation
            }
        }

        _loopCts?.Dispose();
        _loopCts = null;
        _loopTask = null;
    }

    public Task<CameraFrame> CaptureStillAsync(CancellationToken cancellationToken = default)
    {
        // A "still" gets a touch more resolution than the live preview to emulate a real camera's
        // higher-quality still-capture mode.
        var frame = RenderFrame(Width, Height, Interlocked.Increment(ref _frameNumber));
        return Task.FromResult(frame);
    }

    private async Task RunLoopAsync(int fps, CancellationToken token)
    {
        var frameInterval = TimeSpan.FromSeconds(1.0 / fps);
        var frameCountForFps = 0;
        var fpsWindowStart = _clock.Elapsed;

        using var timer = new PeriodicTimer(frameInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                var frame = RenderFrame(Width, Height, Interlocked.Increment(ref _frameNumber));
                _previewFrames.Publish(frame);

                frameCountForFps++;
                var elapsed = _clock.Elapsed - fpsWindowStart;
                if (elapsed.TotalSeconds >= 1)
                {
                    ActualFps = frameCountForFps / elapsed.TotalSeconds;
                    frameCountForFps = 0;
                    fpsWindowStart = _clock.Elapsed;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // normal shutdown path
        }
    }

    private CameraFrame RenderFrame(int width, int height, long frameNumber)
    {
        var image = new Image<Rgba32>(width, height);
        var t = _clock.Elapsed.TotalSeconds;

        image.Mutate(ctx =>
        {
            ctx.Fill(GreenScreenColor);

            // A subtle horizontal gradient band so the chroma-key feather settings have something
            // other than a flat color to react to.
            var bandHeight = height / 6;
            ctx.Fill(GreenScreenColor.WithAlpha(0.6f), new RectangleF(0, height - bandHeight, width, bandHeight));

            // "Person": a moving skin-tone ellipse (head) plus a torso rectangle, swaying left/right.
            var swayX = (float)(Math.Sin(t * 0.8) * width * 0.15);
            var centerX = (width / 2f) + swayX;
            var headRadius = height * 0.09f;
            var headCenter = new PointF(centerX, height * 0.32f);
            ctx.Fill(SkinTone, new EllipsePolygon(headCenter, headRadius));

            var torsoWidth = width * 0.22f;
            var torsoHeight = height * 0.38f;
            var torsoRect = new RectangleF(centerX - (torsoWidth / 2), height * 0.40f, torsoWidth, torsoHeight);
            ctx.Fill(Color.FromRgb(60, 80, 160), torsoRect);
        });

        return new CameraFrame
        {
            Image = image,
            TimestampUtc = DateTimeOffset.UtcNow,
            FrameNumber = frameNumber,
        };
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _previewFrames.Dispose();
    }
}
