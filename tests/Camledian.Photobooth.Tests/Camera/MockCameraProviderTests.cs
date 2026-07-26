using Camledian.Photobooth.Camera;
using Camledian.Photobooth.Camera.Providers;

namespace Camledian.Photobooth.Tests.Camera;

public class MockCameraProviderTests
{
    [Fact]
    public void ListDevices_ReturnsExactlyOneMockDevice()
    {
        var provider = new MockCameraProvider();
        var devices = provider.ListDevices();

        Assert.Single(devices);
        Assert.Equal("Mock Camera", devices[0].Name);
    }

    [Fact]
    public async Task StartAsync_ProducesFramesAtRequestedResolution()
    {
        await using var provider = new MockCameraProvider();
        await provider.StartAsync(new CameraStartOptions(null, 320, 180, 30));

        var frame = await provider.PreviewFrames.WaitNextAsync(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);

        Assert.NotNull(frame);
        Assert.Equal(320, frame!.Image.Width);
        Assert.Equal(180, frame.Image.Height);
        frame.Dispose();

        await provider.StopAsync();
    }

    [Fact]
    public async Task CaptureStillAsync_ReturnsAUsableFrameWithoutStarting()
    {
        await using var provider = new MockCameraProvider();

        var still = await provider.CaptureStillAsync();

        Assert.True(still.Image.Width > 0);
        Assert.True(still.Image.Height > 0);
        still.Dispose();
    }

    [Fact]
    public async Task LatestFrameBox_AlwaysServesTheNewestFrameNotABacklog()
    {
        // The core "latest wins, older frames get disposed" invariant is proven deterministically
        // (no real timing involved) in Core/LatestFrameBoxTests.cs. This test just checks the
        // integration with the camera's real background loop, so it deliberately measures relative
        // progress from a dynamically-observed starting point rather than assuming a fixed frame
        // number is reached within a fixed delay (that assumption was flaky under CI scheduling load).
        await using var provider = new MockCameraProvider();
        await provider.StartAsync(new CameraStartOptions(null, 160, 90, 60));
        using var overallCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var first = await provider.PreviewFrames.WaitNextAsync(overallCts.Token);
        Assert.NotNull(first);
        var firstFrameNumber = first!.FrameNumber;
        first.Dispose();

        // Let a backlog build up (at 60 FPS, well over one frame) without consuming any of it.
        await Task.Delay(300, overallCts.Token);

        var later = await provider.PreviewFrames.WaitNextAsync(overallCts.Token);
        Assert.NotNull(later);
        Assert.True(
            later!.FrameNumber > firstFrameNumber + 1,
            $"expected to have skipped a backlog of buffered frames (first={firstFrameNumber}, later={later.FrameNumber})");
        later.Dispose();

        await provider.StopAsync();
    }
}
