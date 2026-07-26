using Camledian.Photobooth.Core.Models;
using Camledian.Photobooth.Storage;
using Camledian.Photobooth.Storage.Repositories;
using Microsoft.Extensions.Logging;

namespace Camledian.Photobooth.Cloud.Services;

/// <summary>
/// Diffs the cloud asset manifest against what's already stored locally by content hash (spec §37)
/// and only downloads assets that are new or changed — never blocking capture on the result, since
/// this runs against whatever backgrounds/overlays are already on disk from a previous sync or the
/// bundled demo set.
/// </summary>
public class AssetManifestSyncService(
    CloudApiClient apiClient,
    AssetRepository assetRepository,
    StorageSettings storageSettings,
    HttpClient downloadClient,
    ILogger<AssetManifestSyncService> logger)
{
    public async Task SyncEventAssetsAsync(string deviceToken, string eventId, CancellationToken cancellationToken = default)
    {
        var manifest = await apiClient.GetEventAssetsAsync(deviceToken, eventId, cancellationToken).ConfigureAwait(false);

        var existingBackgrounds = await assetRepository.ListAsync(AssetType.Background, cancellationToken).ConfigureAwait(false);
        var existingOverlays = await assetRepository.ListAsync(AssetType.Overlay, cancellationToken).ConfigureAwait(false);
        var existingHashById = existingBackgrounds.Concat(existingOverlays).ToDictionary(a => a.Id, a => a.Hash);

        var syncedDirectory = Path.Combine(StoragePaths.Resolve(storageSettings.CacheDirectory), "synced-assets");
        Directory.CreateDirectory(syncedDirectory);

        foreach (var entry in manifest.Assets)
        {
            if (existingHashById.TryGetValue(entry.Id, out var existingHash) && existingHash == entry.Hash)
            {
                continue; // unchanged — skip the download entirely
            }

            try
            {
                var extension = Path.GetExtension(new Uri(entry.Url).AbsolutePath) is { Length: > 1 } ext ? ext : ".png";
                var localPath = Path.Combine(syncedDirectory, $"{entry.Id}{extension}");

                var bytes = await downloadClient.GetByteArrayAsync(entry.Url, cancellationToken).ConfigureAwait(false);
                await File.WriteAllBytesAsync(localPath, bytes, cancellationToken).ConfigureAwait(false);

                var assetType = entry.Type == "overlay" ? AssetType.Overlay : AssetType.Background;
                await assetRepository.UpsertAsync(
                    new AssetRecord
                    {
                        Id = entry.Id,
                        Type = assetType,
                        Name = entry.Name,
                        LocalPath = localPath,
                        Hash = entry.Hash,
                        SourceUrl = entry.Url,
                    },
                    cancellationToken).ConfigureAwait(false);

                logger.LogInformation("Synced {Type} asset '{Name}' ({Id}) from cloud.", entry.Type, entry.Name, entry.Id);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to sync asset {Id} from cloud; keeping whatever local copy already exists.", entry.Id);
            }
        }
    }
}
