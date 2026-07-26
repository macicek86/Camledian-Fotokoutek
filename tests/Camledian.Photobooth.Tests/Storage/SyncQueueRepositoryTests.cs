using Camledian.Photobooth.Core.Models;
using Camledian.Photobooth.Storage.Repositories;

namespace Camledian.Photobooth.Tests.Storage;

public class SyncQueueRepositoryTests : IClassFixture<TempDatabaseFixture>
{
    private readonly TempDatabaseFixture _fixture;

    public SyncQueueRepositoryTests(TempDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public void ComputeNextAttempt_GrowsExponentiallyUpToCeiling()
    {
        var first = SyncQueueItem.ComputeNextAttempt(1, baseDelaySeconds: 5, maxDelaySeconds: 900);
        var second = SyncQueueItem.ComputeNextAttempt(2, baseDelaySeconds: 5, maxDelaySeconds: 900);
        var third = SyncQueueItem.ComputeNextAttempt(3, baseDelaySeconds: 5, maxDelaySeconds: 900);
        var huge = SyncQueueItem.ComputeNextAttempt(20, baseDelaySeconds: 5, maxDelaySeconds: 900);

        var now = DateTimeOffset.UtcNow;
        var firstDelay = (first - now).TotalSeconds;
        var secondDelay = (second - now).TotalSeconds;
        var thirdDelay = (third - now).TotalSeconds;
        var hugeDelay = (huge - now).TotalSeconds;

        Assert.InRange(firstDelay, 4, 6); // attempts=1 -> base * 2^0 = 5s
        Assert.InRange(secondDelay, 9, 11); // attempts=2 -> base * 2^1 = 10s
        Assert.InRange(thirdDelay, 19, 21); // attempts=3 -> base * 2^2 = 20s
        Assert.InRange(hugeDelay, 895, 905); // capped at maxDelaySeconds
    }

    [Fact]
    public async Task GetDueAsync_OnlyReturnsPendingOrFailedItemsAtOrBeforeNow()
    {
        var repo = new SyncQueueRepository(_fixture.ConnectionFactory);

        await repo.EnqueueAsync(new SyncQueueItem
        {
            Id = "due-now",
            PhotoId = "p1",
            Status = SyncStatus.Pending,
            NextAttemptAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1),
        });
        await repo.EnqueueAsync(new SyncQueueItem
        {
            Id = "not-due-yet",
            PhotoId = "p2",
            Status = SyncStatus.Failed,
            NextAttemptAtUtc = DateTimeOffset.UtcNow.AddMinutes(10),
        });
        await repo.EnqueueAsync(new SyncQueueItem
        {
            Id = "already-uploaded",
            PhotoId = "p3",
            Status = SyncStatus.Uploaded,
            NextAttemptAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1),
        });

        var due = await repo.GetDueAsync();

        Assert.Contains(due, i => i.Id == "due-now");
        Assert.DoesNotContain(due, i => i.Id == "not-due-yet");
        Assert.DoesNotContain(due, i => i.Id == "already-uploaded");
    }

    [Fact]
    public async Task ForceRetryAllAsync_MakesFailedItemsDueImmediately()
    {
        var repo = new SyncQueueRepository(_fixture.ConnectionFactory);
        await repo.EnqueueAsync(new SyncQueueItem
        {
            Id = "stuck",
            PhotoId = "p4",
            Status = SyncStatus.Failed,
            NextAttemptAtUtc = DateTimeOffset.MaxValue,
        });

        await repo.ForceRetryAllAsync();
        var due = await repo.GetDueAsync();

        Assert.Contains(due, i => i.Id == "stuck");
    }

    [Fact]
    public async Task CountPendingAsync_CountsPendingFailedAndUploadingButNotUploaded()
    {
        using var fixture = new TempDatabaseFixture();
        var repo = new SyncQueueRepository(fixture.ConnectionFactory);

        await repo.EnqueueAsync(new SyncQueueItem { Id = "a", PhotoId = "p", Status = SyncStatus.Pending, NextAttemptAtUtc = DateTimeOffset.UtcNow });
        await repo.EnqueueAsync(new SyncQueueItem { Id = "b", PhotoId = "p", Status = SyncStatus.Uploading, NextAttemptAtUtc = DateTimeOffset.UtcNow });
        await repo.EnqueueAsync(new SyncQueueItem { Id = "c", PhotoId = "p", Status = SyncStatus.Uploaded, NextAttemptAtUtc = DateTimeOffset.UtcNow });

        var count = await repo.CountPendingAsync();

        Assert.Equal(2, count);
    }
}
