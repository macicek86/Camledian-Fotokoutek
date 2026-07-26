using System.IO;
using Camledian.Photobooth.Core.Models;
using Camledian.Photobooth.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;

namespace Camledian.Photobooth.App.ViewModels;

/// <summary>
/// Admin > "background subtraction" workflow: capture one photo of the empty scene, so later frames
/// can be keyed against it without needing a green screen at all (the booth/camera don't move during
/// an event, so a single reference photo stays valid for the whole event).
/// </summary>
public partial class MainViewModel
{
    [ObservableProperty]
    private string _backgroundReferenceStatus = "Zatím nevyfoceno.";

    [RelayCommand]
    private async Task CaptureBackgroundReferenceAsync()
    {
        var still = await _camera.CaptureStillAsync().ConfigureAwait(true);
        try
        {
            var cacheDirectory = StoragePaths.Resolve(_settingsService.Current.Storage.CacheDirectory);
            Directory.CreateDirectory(cacheDirectory);

            // A fresh filename per capture (rather than overwriting one fixed path) so
            // BackgroundSubtractionRemovalService's path-keyed cache reloads it automatically.
            var fileName = $"background-reference-{DateTimeOffset.UtcNow.Ticks}.jpg";
            var newPath = Path.Combine(cacheDirectory, fileName);
            await still.Image.SaveAsJpegAsync(newPath).ConfigureAwait(true);

            var previousPath = _settingsService.Current.BackgroundSubtraction.ReferenceImagePath;
            _settingsService.Current.BackgroundSubtraction.ReferenceImagePath = newPath;
            await _settingsService
                .SaveSectionAsync(nameof(AppSettings.BackgroundSubtraction), _settingsService.Current.BackgroundSubtraction)
                .ConfigureAwait(true);

            if (!string.IsNullOrWhiteSpace(previousPath) && previousPath != newPath && File.Exists(previousPath))
            {
                try
                {
                    File.Delete(previousPath);
                }
                catch (IOException)
                {
                    // best-effort cleanup of the old reference file
                }
            }

            BackgroundReferenceStatus = $"Vyfoceno {DateTimeOffset.Now:HH:mm:ss}. Přepněte režim na \"BackgroundSubtraction\".";
            _logger.LogInformation("Captured background-subtraction reference photo at {Path}.", newPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to capture background-subtraction reference photo.");
            BackgroundReferenceStatus = "Vyfocení selhalo: " + ex.Message;
        }
        finally
        {
            still.Dispose();
        }
    }
}
