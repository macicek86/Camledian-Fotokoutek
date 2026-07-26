using System.Diagnostics;
using System.Runtime.InteropServices;
using Camledian.Photobooth.Core.Utilities;
using OpenCvSharp;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Camledian.Photobooth.Camera.Providers;

/// <summary>
/// Real webcam capture via OpenCvSharp (DirectShow/Media Foundation backend on Windows). This is
/// the production provider — it requires an actual Windows machine with a camera to run; it will
/// restore and compile in this Linux devcontainer (that's what CI/local builds verify) but opening
/// a device here would fail, which is exactly why <see cref="MockCameraProvider"/> exists for
/// pipeline development and automated tests.
/// </summary>
public sealed class WebcamCameraProvider : ICameraProvider
{
    private const int MaxProbeIndex = 9;

    private readonly LatestFrameBox<CameraFrame> _previewFrames = new();
    private VideoCapture? _capture;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private long _frameNumber;

    public string Name { get; private set; } = "Webcam";
    public bool IsRunning { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public double ActualFps { get; private set; }
    public LatestFrameBox<CameraFrame> PreviewFrames => _previewFrames;

    /// <summary>
    /// OpenCvSharp has no cross-platform "friendly device name" API, so devices are discovered by
    /// probing indices 0..9 and checking whether they open. On Windows this is a well established
    /// technique for DirectShow/MSMF-backed capture; giving devices human-friendly names would
    /// require additional Media Foundation/WMI enumeration, left as a future improvement.
    /// </summary>
    public IReadOnlyList<CameraDeviceInfo> ListDevices()
    {
        var devices = new List<CameraDeviceInfo>();
        for (var index = 0; index <= MaxProbeIndex; index++)
        {
            using var probe = new VideoCapture(index);
            if (probe.IsOpened())
            {
                devices.Add(new CameraDeviceInfo($"cam-{index}", $"Camera {index}", index));
            }
        }

        return devices;
    }

    public Task StartAsync(CameraStartOptions options, CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            return Task.CompletedTask;
        }

        var index = ResolveDeviceIndex(options.DeviceId);
        var capture = new VideoCapture(index);
        if (!capture.IsOpened())
        {
            capture.Dispose();
            throw new InvalidOperationException(
                $"Could not open camera at index {index}. On a Linux devcontainer this is expected " +
                "(no camera hardware/driver present) — use MockCameraProvider for development, or run " +
                "on a real Windows machine.");
        }

        capture.Set(VideoCaptureProperties.FrameWidth, options.Width);
        capture.Set(VideoCaptureProperties.FrameHeight, options.Height);
        capture.Set(VideoCaptureProperties.Fps, options.Fps);

        Width = (int)capture.Get(VideoCaptureProperties.FrameWidth);
        Height = (int)capture.Get(VideoCaptureProperties.FrameHeight);
        Name = $"Camera {index}";

        _capture = capture;
        _loopCts = new CancellationTokenSource();
        IsRunning = true;
        _loopTask = Task.Run(() => RunLoopAsync(_loopCts.Token), CancellationToken.None);
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

        _capture?.Release();
        _capture?.Dispose();
        _capture = null;
    }

    public async Task<CameraFrame> CaptureStillAsync(CancellationToken cancellationToken = default)
    {
        // Reuse the freshest already-decoded preview frame rather than issuing a second, competing
        // Read() against the capture loop's background thread.
        var frame = await _previewFrames.WaitNextAsync(cancellationToken).ConfigureAwait(false);
        if (frame is null)
        {
            throw new InvalidOperationException("No camera frame available to capture.");
        }

        return frame;
    }

    private async Task RunLoopAsync(CancellationToken token)
    {
        var capture = _capture ?? throw new InvalidOperationException("Camera not started.");
        var stopwatch = Stopwatch.StartNew();
        var frameCountForFps = 0;
        var fpsWindowStart = stopwatch.Elapsed;

        using var mat = new Mat();
        using var rgbaMat = new Mat();

        while (!token.IsCancellationRequested)
        {
            var read = await Task.Run(() => capture.Read(mat), token).ConfigureAwait(false);
            if (!read || mat.Empty())
            {
                await Task.Delay(10, token).ConfigureAwait(false);
                continue;
            }

            Cv2.CvtColor(mat, rgbaMat, ColorConversionCodes.BGR2RGBA);
            var byteCount = (int)(rgbaMat.Total() * rgbaMat.ElemSize());
            var buffer = new byte[byteCount];
            Marshal.Copy(rgbaMat.Data, buffer, 0, byteCount);

            var image = Image.LoadPixelData<Rgba32>(buffer, rgbaMat.Cols, rgbaMat.Rows);
            _previewFrames.Publish(new CameraFrame
            {
                Image = image,
                TimestampUtc = DateTimeOffset.UtcNow,
                FrameNumber = Interlocked.Increment(ref _frameNumber),
            });

            frameCountForFps++;
            var elapsed = stopwatch.Elapsed - fpsWindowStart;
            if (elapsed.TotalSeconds >= 1)
            {
                ActualFps = frameCountForFps / elapsed.TotalSeconds;
                frameCountForFps = 0;
                fpsWindowStart = stopwatch.Elapsed;
            }
        }
    }

    private static int ResolveDeviceIndex(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return 0;
        }

        var digits = deviceId.Replace("cam-", string.Empty);
        return int.TryParse(digits, out var index) ? index : 0;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _previewFrames.Dispose();
    }
}
