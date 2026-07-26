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
        await using var provider = new MockCameraProvider();
        await provider.StartAsync(new CameraStartOptions(null, 160, 90, 60));

        // Let a handful of frames pile up in the background loop without consuming any.
        await Task.Delay(300);

        var frame = await provider.PreviewFrames.WaitNextAsync(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);

        Assert.NotNull(frame);
        // The whole point of LatestFrameBox is that only one frame is ever buffered; if a backlog
        // built up we'd still only get the most recent one here, not frame #1.
        Assert.True(frame!.FrameNumber > 1, "expected a later frame, not the very first one, proving backlog was dropped");
        frame.Dispose();

        await provider.StopAsync();
    }
}
