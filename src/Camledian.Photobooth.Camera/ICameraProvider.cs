using Camledian.Photobooth.Core.Utilities;

namespace Camledian.Photobooth.Camera;

/// <summary>
/// Abstraction over "something that produces camera frames" (spec §8). WebcamCameraProvider is the
/// real implementation (OpenCvSharp/DirectShow-MSMF on Windows); MockCameraProvider needs no
/// hardware at all and is what runs in this Linux devcontainer and in tests.
/// </summary>
public interface ICameraProvider : IAsyncDisposable
{
    string Name { get; }
    bool IsRunning { get; }
    int Width { get; }
    int Height { get; }
    double ActualFps { get; }

    /// <summary>Single-consumer "latest frame wins" mailbox feeding the live preview pipeline.</summary>
    LatestFrameBox<CameraFrame> PreviewFrames { get; }

    IReadOnlyList<CameraDeviceInfo> ListDevices();

    Task StartAsync(CameraStartOptions options, CancellationToken cancellationToken = default);

    Task StopAsync();

    /// <summary>Grabs one frame to use as the actual photo — never a UI screenshot, per spec §15.</summary>
    Task<CameraFrame> CaptureStillAsync(CancellationToken cancellationToken = default);
}
