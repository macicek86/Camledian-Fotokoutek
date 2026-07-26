using Camledian.Photobooth.Camera;
using Camledian.Photobooth.Core.Models;
using Camledian.Photobooth.Core.Utilities;
using Camledian.Photobooth.Imaging;
using Camledian.Photobooth.Imaging.Branding;
using Camledian.Photobooth.Imaging.Composition;
using Camledian.Photobooth.Storage;
using Camledian.Photobooth.Storage.Repositories;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace Camledian.Photobooth.App.Services;

/// <summary>
/// The "Vyfotit" -&gt; final photo pipeline (spec §15): grab a real capture frame (never a UI
/// screenshot), run the *high quality* background-removal pass over it, composite at final
/// resolution, and persist both the original and the final render under data/photos/&lt;date&gt;/.
/// </summary>
public class PhotoCaptureService(
    ICameraProvider camera,
    BackgroundRemovalServiceFactory backgroundRemovalFactory,
    ImageCompositionService compositionService,
    PhotoFileStore fileStore,
    PhotoRepository photoRepository,
    SyncQueueRepository syncQueueRepository,
    SettingsService settingsService,
    ILogger<PhotoCaptureService> logger)
{
    public async Task<PhotoRecord> CaptureAsync(
        string eventId,
        Image<Rgba32> background,
        Image<Rgba32>? overlay,
        OutputTemplate template,
        CancellationToken cancellationToken = default)
    {
        var still = await camera.CaptureStillAsync(cancellationToken).ConfigureAwait(false);
        var photoId = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;

        try
        {
            var originalPath = fileStore.GetOriginalPath(photoId, now, "jpg");
            await still.Image.SaveAsJpegAsync(originalPath, new JpegEncoder { Quality = 95 }, cancellationToken)
                .ConfigureAwait(false);

            var backgroundRemoval = backgroundRemovalFactory.Resolve();
            logger.LogInformation("Captured photo {PhotoId}, running {Provider} at final quality.", photoId, backgroundRemoval.Name);
            await backgroundRemoval.ApplyAsync(still.Image, highQuality: true, cancellationToken).ConfigureAwait(false);

            using var finalImage = compositionService.ComposeFinal(background, still.Image, overlay, template);

            try
            {
                // Branding (corner logo + text banner) goes on last, over everything — and a broken
                // logo path or missing font must never cost the guest their photo.
                BrandingRenderer.Apply(finalImage, settingsService.Current.Branding);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Branding failed for photo {PhotoId}; saving it without branding.", photoId);
            }

            var finalPath = fileStore.GetFinalPath(photoId, now, "jpg");
            await finalImage.SaveAsJpegAsync(finalPath, new JpegEncoder { Quality = 93 }, cancellationToken)
                .ConfigureAwait(false);

            var record = new PhotoRecord
            {
                Id = photoId,
                EventId = eventId,
                OriginalPath = originalPath,
                FinalPath = finalPath,
                CreatedAtUtc = now,
            };
            await photoRepository.InsertAsync(record, cancellationToken).ConfigureAwait(false);

            if (settingsService.Current.Cloud.Enabled)
            {
                await syncQueueRepository.EnqueueAsync(new SyncQueueItem
                {
                    Id = TokenGenerator.CreateDownloadToken(16),
                    PhotoId = photoId,
                    Status = SyncStatus.Pending,
                    NextAttemptAtUtc = DateTimeOffset.UtcNow,
                }, cancellationToken).ConfigureAwait(false);
            }

            return record;
        }
        finally
        {
            still.Dispose();
        }
    }
}
