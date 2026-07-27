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
/// The "Vyfotit" -&gt; final photo pipeline (spec §15), split in two because of burst mode: first
/// grab one or more real capture frames (never a UI screenshot), then — once the guest has picked
/// their favorite — run the *high quality* background-removal pass over it, composite at final
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
    /// <summary>Takes <paramref name="count"/> stills with a pause between them (burst, spec §57
    /// "více fotografií"). Caller owns and must dispose every returned frame. A per-shot progress
    /// callback lets the UI update its "Snímek 2/3" counter between shots.</summary>
    public async Task<IReadOnlyList<CameraFrame>> CaptureBurstAsync(
        int count,
        TimeSpan interval,
        Action<int>? onShotCaptured = null,
        CancellationToken cancellationToken = default)
    {
        var frames = new List<CameraFrame>(count);
        try
        {
            for (var i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
                }

                frames.Add(await camera.CaptureStillAsync(cancellationToken).ConfigureAwait(false));
                onShotCaptured?.Invoke(i + 1);
            }
        }
        catch
        {
            foreach (var frame in frames)
            {
                frame.Dispose();
            }

            throw;
        }

        return frames;
    }

    /// <summary>Processes one (already captured, guest-chosen) frame into the stored photo. Does
    /// NOT dispose <paramref name="still"/> — the caller owns the whole burst and disposes it once
    /// the selection flow is over.</summary>
    public async Task<PhotoRecord> ProcessCapturedAsync(
        string eventId,
        CameraFrame still,
        Image<Rgba32> background,
        Image<Rgba32>? overlay,
        OutputTemplate template,
        CancellationToken cancellationToken = default)
    {
        var photoId = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;

        var originalPath = fileStore.GetOriginalPath(photoId, now, "jpg");
        await still.Image.SaveAsJpegAsync(originalPath, new JpegEncoder { Quality = 95 }, cancellationToken)
            .ConfigureAwait(false);

        var backgroundRemoval = backgroundRemovalFactory.Resolve();
        logger.LogInformation("Processing photo {PhotoId} with {Provider} at final quality.", photoId, backgroundRemoval.Name);
        await backgroundRemoval.ApplyAsync(still.Image, BackgroundRemovalOptions.FinalRender, cancellationToken).ConfigureAwait(false);

        using var finalImage = compositionService.ComposeFinal(background, still.Image, overlay, template);

        try
        {
            // Branding (logo + text banner) goes on last, over everything — and a broken logo path
            // or missing font must never cost the guest their photo.
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
}
