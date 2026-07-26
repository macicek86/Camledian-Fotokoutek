namespace Camledian.Photobooth.Core.Models;

public enum SyncStatus
{
    Pending,
    Uploading,
    Uploaded,
    Failed,
}

/// <summary>Persistent (SQLite-backed) upload queue entry. A photo is always saved locally first;
/// this record is what survives an app restart so the background sync worker can resume (spec §38).</summary>
public class SyncQueueItem
{
    public required string Id { get; init; }
    public required string PhotoId { get; init; }
    public SyncStatus Status { get; set; } = SyncStatus.Pending;
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Exponential backoff with a configurable ceiling.</summary>
    public static DateTimeOffset ComputeNextAttempt(int attempts, int baseDelaySeconds, int maxDelaySeconds)
    {
        var delaySeconds = Math.Min(maxDelaySeconds, baseDelaySeconds * Math.Pow(2, Math.Max(0, attempts - 1)));
        return DateTimeOffset.UtcNow.AddSeconds(delaySeconds);
    }
}
