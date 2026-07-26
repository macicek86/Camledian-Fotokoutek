using System.IO;
using System.Windows.Threading;
using Camledian.Photobooth.AI;
using Camledian.Photobooth.Cloud.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Camledian.Photobooth.App.ViewModels;

/// <summary>
/// The Diagnostics tab (spec §44): live camera/preview/AI/printer/cloud status plus "Test X" /
/// "Sync now" buttons. Split into its own partial-class file purely to keep MainViewModel.cs
/// focused on the core state-machine-driven workflow.
/// </summary>
public partial class MainViewModel
{
    private DispatcherTimer? _diagnosticsTimer;

    [ObservableProperty]
    private string _diagCameraStatus = "-";

    [ObservableProperty]
    private double _diagPreviewFps;

    [ObservableProperty]
    private double _diagChromaOrAiMs;

    [ObservableProperty]
    private double _diagCompositionMs;

    [ObservableProperty]
    private string _diagPrinterStatus = "-";

    [ObservableProperty]
    private string _diagCloudStatus = "-";

    [ObservableProperty]
    private int _diagSyncQueueCount;

    [ObservableProperty]
    private string _diagDiskFree = "-";

    [ObservableProperty]
    private string? _diagTestMessage;

    private void StartDiagnosticsTimer()
    {
        _ = RefreshDiagnosticsAsync();

        if (_diagnosticsTimer is null)
        {
            _diagnosticsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _diagnosticsTimer.Tick += (_, _) => _ = RefreshDiagnosticsAsync();
        }

        _diagnosticsTimer.Start();
    }

    private void StopDiagnosticsTimer() => _diagnosticsTimer?.Stop();

    private async Task RefreshDiagnosticsAsync()
    {
        DiagCameraStatus = _camera.IsRunning ? $"{_camera.Name} ({_camera.Width}x{_camera.Height})" : "Nepřipojena";
        DiagPreviewFps = _camera.ActualFps;
        DiagChromaOrAiMs = _preview.LastChromaKeyOrAiMs;
        DiagCompositionMs = _preview.LastCompositionMs;
        DiagPrinterStatus = AvailablePrinters.Count > 0
            ? string.Join(", ", AvailablePrinters.Select(p => p.Name))
            : "Žádná tiskárna nenalezena";

        // Spec §46: AI model missing -> warn and keep going on Green Screen instead of failing.
        if (_backgroundRemovalFactory.LastFallbackNotice is { } notice && DiagTestMessage != notice)
        {
            DiagTestMessage = notice;
        }

        var cloud = _settingsService.Current.Cloud;
        DiagCloudStatus = !cloud.Enabled
            ? "Vypnuto"
            : string.IsNullOrWhiteSpace(cloud.DeviceToken) ? "Nespárováno" : "Spárováno";

        try
        {
            DiagSyncQueueCount = await _syncQueueRepository.CountPendingAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read sync queue count for diagnostics.");
        }

        try
        {
            var root = Path.GetPathRoot(AppContext.BaseDirectory);
            if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
            {
                var drive = new DriveInfo(root);
                var freeGb = drive.AvailableFreeSpace / 1_000_000_000.0;
                DiagDiskFree = freeGb < _settingsService.Current.Storage.MinFreeDiskSpaceWarningGb
                    ? $"{freeGb:0.0} GB (málo místa!)"
                    : $"{freeGb:0.0} GB";
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read disk free space for diagnostics.");
            DiagDiskFree = "?";
        }
    }

    [RelayCommand]
    private void TestCamera()
    {
        AvailableCameras = _camera.ListDevices();
        OnPropertyChanged(nameof(AvailableCameras));
        DiagTestMessage = $"Nalezeno {AvailableCameras.Count} zařízení. Aktuální: " +
            (_camera.IsRunning ? $"{_camera.Name} ({_camera.Width}x{_camera.Height}, {_camera.ActualFps:0.0} FPS)" : "neběží") + ".";
    }

    [RelayCommand]
    private async Task TestCaptureAsync()
    {
        try
        {
            var frame = await _camera.CaptureStillAsync().ConfigureAwait(true);
            var width = frame.Image.Width;
            var height = frame.Image.Height;
            frame.Dispose();
            DiagTestMessage = $"Test snímku proběhl úspěšně ({width}x{height}).";
        }
        catch (Exception ex)
        {
            DiagTestMessage = "Test snímku selhal: " + ex.Message;
        }
    }

    [RelayCommand]
    private async Task TestAiAsync()
    {
        DiagTestMessage = "Spouštím AI benchmark...";
        try
        {
            var result = await AiBenchmarkRunner.RunAsync(_backgroundRemovalFactory.AiProvider, iterations: 5).ConfigureAwait(true);
            DiagTestMessage = $"AI benchmark: min {result.MinMs:0} ms, průměr {result.AvgMs:0} ms, max {result.MaxMs:0} ms ({result.Iterations} běhů).";
        }
        catch (Exception ex)
        {
            DiagTestMessage = "AI test selhal: " + ex.Message + " (je stažený model? scripts/download-models.ps1)";
        }
    }

    [RelayCommand]
    private async Task TestPrintAsync()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"camledian-print-test-{Guid.NewGuid():N}.jpg");
        try
        {
            using (var testImage = new Image<Rgba32>(1200, 1800))
            {
                testImage.Mutate(ctx =>
                {
                    ctx.Fill(Color.White);
                    ctx.Fill(Color.FromRgb(212, 175, 55), new RectangleF(60, 60, 1080, 1680));
                    ctx.Fill(Color.White, new RectangleF(120, 120, 960, 1560));
                });
                await testImage.SaveAsJpegAsync(tempPath).ConfigureAwait(true);
            }

            var result = await _printingService.PrintAsync(tempPath, _settingsService.Current.Print).ConfigureAwait(true);
            DiagTestMessage = result.Success ? "Testovací tisk odeslán." : "Testovací tisk selhal: " + result.ErrorMessage;
        }
        catch (Exception ex)
        {
            DiagTestMessage = "Testovací tisk selhal: " + ex.Message;
        }
        finally
        {
            try
            {
                File.Delete(tempPath);
            }
            catch (IOException)
            {
                // best-effort cleanup of the scratch test file
            }
        }
    }

    [RelayCommand]
    private async Task TestReceiptPrinterAsync()
    {
        var settings = _settingsService.Current.ReceiptPrinter;
        try
        {
            var modules = QrCodeService.GenerateModules("https://fotokoutek.camledian.art/foto/test");
            var result = await _receiptPrinterService.PrintQrSlipAsync(modules, settings).ConfigureAwait(true);
            DiagTestMessage = result.Success ? "Testovací tisk QR kódu odeslán." : "Testovací tisk QR kódu selhal: " + result.ErrorMessage;
        }
        catch (Exception ex)
        {
            DiagTestMessage = "Testovací tisk QR kódu selhal: " + ex.Message;
        }
    }

    [RelayCommand]
    private async Task TestCloudAsync()
    {
        var cloud = _settingsService.Current.Cloud;
        if (!cloud.Enabled || string.IsNullOrWhiteSpace(cloud.DeviceToken))
        {
            DiagTestMessage = "Cloud není povolen nebo zařízení není spárováno.";
            return;
        }

        try
        {
            var response = await _cloudApiClient.HeartbeatAsync(cloud.DeviceToken, "diagnostics-test").ConfigureAwait(true);
            DiagTestMessage = $"Cloud odpověděl OK (server čas {response.ServerTimeUtc:HH:mm:ss} UTC).";
            DiagCloudStatus = "Online";
        }
        catch (Exception ex)
        {
            DiagTestMessage = "Cloud test selhal: " + ex.Message;
            DiagCloudStatus = "Offline";
        }
    }

    [RelayCommand]
    private async Task SyncNowAsync()
    {
        try
        {
            await _syncQueueRepository.ForceRetryAllAsync().ConfigureAwait(true);
            DiagTestMessage = "Synchronizace fronty spuštěna — čeká se na další cyklus CloudSyncWorker.";
            await RefreshDiagnosticsAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            DiagTestMessage = "Sync now selhalo: " + ex.Message;
        }
    }
}
