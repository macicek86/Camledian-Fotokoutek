using Camledian.Photobooth.Core.Utilities;

namespace Camledian.Photobooth.Tests.Core;

/// <summary>Deterministic tests for the "latest frame wins" mailbox — no real threads/timing
/// involved, unlike testing this invariant indirectly through MockCameraProvider's background loop
/// (which is inherently timing-sensitive and can be flaky under CI scheduling load).</summary>
public class LatestFrameBoxTests
{
    private sealed class DisposableStub : IDisposable
    {
        public int Id { get; }
        public bool IsDisposed { get; private set; }

        public DisposableStub(int id) => Id = id;

        public void Dispose() => IsDisposed = true;
    }

    [Fact]
    public async Task WaitNextAsync_ReturnsThePublishedValue()
    {
        using var box = new LatestFrameBox<DisposableStub>();
        box.Publish(new DisposableStub(1));

        var result = await box.WaitNextAsync();

        Assert.NotNull(result);
        Assert.Equal(1, result!.Id);
    }

    [Fact]
    public async Task PublishingMultipleTimesBeforeConsuming_OnlyTheLatestSurvives()
    {
        using var box = new LatestFrameBox<DisposableStub>();
        var first = new DisposableStub(1);
        var second = new DisposableStub(2);
        var third = new DisposableStub(3);

        box.Publish(first);
        box.Publish(second);
        box.Publish(third);

        var result = await box.WaitNextAsync();

        Assert.Equal(3, result!.Id);
    }

    [Fact]
    public void PublishingMultipleTimesBeforeConsuming_DisposesTheOlderOnes()
    {
        using var box = new LatestFrameBox<DisposableStub>();
        var first = new DisposableStub(1);
        var second = new DisposableStub(2);
        var third = new DisposableStub(3);

        box.Publish(first);
        box.Publish(second);
        box.Publish(third);

        Assert.True(first.IsDisposed, "frame 1 should have been disposed when frame 2 was published");
        Assert.True(second.IsDisposed, "frame 2 should have been disposed when frame 3 was published");
        Assert.False(third.IsDisposed, "the latest frame must survive until consumed");
    }

    [Fact]
    public async Task WaitNextAsync_ClearsThePendingSlotAfterReading()
    {
        using var box = new LatestFrameBox<DisposableStub>();
        box.Publish(new DisposableStub(1));
        await box.WaitNextAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var second = await box.WaitNextAsync(cts.Token);

        Assert.Null(second); // nothing new was published, so this should time out via cancellation, not re-return frame 1
    }

    [Fact]
    public async Task WaitNextAsync_ReturnsNullWhenCancelledBeforeAnyPublish()
    {
        using var box = new LatestFrameBox<DisposableStub>();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await box.WaitNextAsync(cts.Token);

        Assert.Null(result);
    }

    [Fact]
    public void Dispose_DisposesAnyStillPendingFrame()
    {
        var box = new LatestFrameBox<DisposableStub>();
        var pending = new DisposableStub(1);
        box.Publish(pending);

        box.Dispose();

        Assert.True(pending.IsDisposed);
    }

    [Fact]
    public void PublishAfterDispose_DisposesTheIncomingFrameInstead()
    {
        var box = new LatestFrameBox<DisposableStub>();
        box.Dispose();

        var lateFrame = new DisposableStub(1);
        box.Publish(lateFrame);

        Assert.True(lateFrame.IsDisposed);
    }
}
