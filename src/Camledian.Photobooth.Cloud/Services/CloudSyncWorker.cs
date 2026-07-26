using Camledian.Photobooth.Core.Models;
using Camledian.Photobooth.Storage.Repositories;
using Microsoft.Extensions.Logging;

namespace Camledian.Photobooth.Cloud.Services;

/// <summary>
/// Background upload worker (spec §38/§39). Photos are always saved locally first (see
/// PhotoCaptureService in the App project) — this worker only ever drains the persistent
/// SQLite-backed queue afterwards, so a photo is never lost even if the device is offline for days.
/// Also sends the periodic heartbeat (spec §32/§44) while it's running.
/// </summary>
public sealed class CloudSyncWorker(
    CloudApiClient apiClient,
    PhotoRepository photoRepository,
    SyncQueueRepository syncQueueRepository,
    Func<CloudSettings> getSettings,
    ILogger<CloudSyncWorker> logger) : IAsyncDisposable
{
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public void Start()
    {
        if (_loopTask is not null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _loopTask = Task.Run(() => RunLoopAsync(_cts.Token));
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

        _cts?.Dispose();
        _cts = null;
        _loopTask = null;
    }

    private async Task RunLoopAsync(CancellationToken token)
    {
        var lastHeartbeatUtc = DateTimeOffset.MinValue;

        while (!token.IsCancellationRequested)
        {
            var settings = getSettings();
            if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.DeviceToken))
            {
                await DelayIgnoringCancellation(TimeSpan.FromSeconds(10), token).ConfigureAwait(false);
                continue;
            }

            try
            {
                if ((DateTimeOffset.UtcNow - lastHeartbeatUtc).TotalSeconds >= settings.HeartbeatIntervalSeconds)
                {
                    await apiClient.HeartbeatAsync(settings.DeviceToken!, "online", token).ConfigureAwait(false);
                    lastHeartbeatUtc = DateTimeOffset.UtcNow;
                }

                var due = await syncQueueRepository.GetDueAsync(token).ConfigureAwait(false);
                foreach (var item in due)
                {
                    if (token.IsCancellationRequested)
                    {
                        break;
                    }

                    await ProcessQueueItemAsync(item, settings, token).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Cloud sync loop iteration failed; will retry on the next tick.");
            }

            await DelayIgnoringCancellation(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
        }
    }

    private async Task ProcessQueueItemAsync(SyncQueueItem item, CloudSettings settings, CancellationToken token)
    {
        item.Status = SyncStatus.Uploading;
        await syncQueueRepository.UpdateAsync(item, token).ConfigureAwait(false);

        try
        {
            var photo = await photoRepository.GetByIdAsync(item.PhotoId, token).ConfigureAwait(false);
            if (photo is null)
            {
                logger.LogWarning("SyncQueue item {ItemId} references missing photo {PhotoId}; dropping it.", item.Id, item.PhotoId);
                item.Status = SyncStatus.Uploaded;
                await syncQueueRepository.UpdateAsync(item, token).ConfigureAwait(false);
                return;
            }

            var created = await apiClient.CreatePhotoAsync(settings.DeviceToken!, "image/jpeg", null, token).ConfigureAwait(false);

            await using (var stream = File.OpenRead(photo.FinalPath))
            {
                await apiClient.UploadPhotoBytesAsync(settings.DeviceToken!, created.UploadUrl, stream, "image/jpeg", token).ConfigureAwait(false);
            }

            var completed = await apiClient.CompleteUploadAsync(settings.DeviceToken!, created.PhotoId, token).ConfigureAwait(false);

            photo.Synced = true;
            photo.CloudPhotoId = completed.PhotoId;
            photo.DownloadToken = completed.DownloadToken;
            photo.DownloadUrl = completed.DownloadUrl;
            await photoRepository.UpdateAsync(photo, token).ConfigureAwait(false);

            item.Status = SyncStatus.Uploaded;
            item.LastError = null;
            await syncQueueRepository.UpdateAsync(item, token).ConfigureAwait(false);
            logger.LogInformation("Uploaded photo {PhotoId} to the cloud as {CloudPhotoId}.", photo.Id, completed.PhotoId);
        }
        catch (Exception ex)
        {
            item.Attempts++;
            item.Status = SyncStatus.Failed;
            item.LastError = ex.Message;

            if (item.Attempts >= settings.UploadMaxAttempts)
            {
                // Stop retrying automatically after the configured ceiling; the photo stays on disk
                // and marked unsynced — a future "Sync now" diagnostics action can reset NextAttemptAtUtc.
                item.NextAttemptAtUtc = DateTimeOffset.MaxValue;
                logger.LogError(ex, "Photo {PhotoId} upload permanently failed after {Attempts} attempts.", item.PhotoId, item.Attempts);
            }
            else
            {
                item.NextAttemptAtUtc = SyncQueueItem.ComputeNextAttempt(
                    item.Attempts, settings.UploadRetryBaseDelaySeconds, settings.UploadRetryMaxDelaySeconds);
                logger.LogWarning(
                    ex, "Photo {PhotoId} upload failed (attempt {Attempts}/{Max}); retrying at {NextAttempt:O}.",
                    item.PhotoId, item.Attempts, settings.UploadMaxAttempts, item.NextAttemptAtUtc);
            }

            await syncQueueRepository.UpdateAsync(item, token).ConfigureAwait(false);
        }
    }

    private static async Task DelayIgnoringCancellation(TimeSpan delay, CancellationToken token)
    {
        try
        {
            await Task.Delay(delay, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}
