using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Camledian.Photobooth.App.Services;
using Camledian.Photobooth.App.Wpf;
using Camledian.Photobooth.Camera;
using Camledian.Photobooth.Cloud;
using Camledian.Photobooth.Cloud.Services;
using Camledian.Photobooth.Core.Models;
using Camledian.Photobooth.Core.StateMachine;
using Camledian.Photobooth.Printing;
using Camledian.Photobooth.Storage.Repositories;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Camledian.Photobooth.App.ViewModels;

/// <summary>
/// Orchestrates the whole kiosk workflow (spec §14/§18/§60): the UI never guesses what to show —
/// it reads <see cref="State"/> off <see cref="PhotoboothStateMachine"/> and this class is the only
/// thing allowed to drive transitions.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly PhotoboothStateMachine _fsm;
    private readonly PreviewPipelineService _preview;
    private readonly PhotoCaptureService _capture;
    private readonly AssetCatalogService _assetCatalog;
    private readonly SettingsService _settingsService;
    private readonly IPrintingService _printingService;
    private readonly IReceiptPrinterService _receiptPrinterService;
    private readonly ICameraProvider _camera;
    private readonly BackgroundRemovalServiceFactory _backgroundRemovalFactory;
    private readonly EventRepository _eventRepository;
    private readonly AssetRepository _assetRepository;
    private readonly PhotoRepository _photoRepository;
    private readonly DeviceRepository _deviceRepository;
    private readonly SyncQueueRepository _syncQueueRepository;
    private readonly CloudApiClient _cloudApiClient;
    private readonly DevicePairingService _devicePairingService;
    private readonly AssetManifestSyncService _assetManifestSyncService;
    private readonly CloudSyncWorker _cloudSyncWorker;
    private readonly Dispatcher _dispatcher;
    private readonly ILogger<MainViewModel> _logger;

    private EventDefinition? _activeEvent;
    private CancellationTokenSource? _countdownCts;
    private CancellationTokenSource? _resultTimeoutCts;
    private CancellationTokenSource? _syncPollCts;
    private CancellationTokenSource? _selectPhotoTimeoutCts;
    private IReadOnlyList<CameraFrame>? _burstFrames;

    [ObservableProperty]
    private PhotoboothState _state = PhotoboothState.Idle;

    [ObservableProperty]
    private BitmapSource? _previewImage;

    [ObservableProperty]
    private int _countdownValue;

    [ObservableProperty]
    private AssetRecord? _selectedBackground;

    [ObservableProperty]
    private BitmapSource? _resultImage;

    [ObservableProperty]
    private PhotoRecord? _resultRecord;

    [ObservableProperty]
    private BitmapSource? _qrCodeImage;

    [ObservableProperty]
    private string _syncStatusMessage = "Fotografie se ukládá lokálně.";

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string _pinInput = string.Empty;

    [ObservableProperty]
    private string? _pinError;

    [ObservableProperty]
    private bool _isAdminUnlocked;

    [ObservableProperty]
    private bool _isKioskMode = true;

    [ObservableProperty]
    private string? _pairingCode;

    [ObservableProperty]
    private string _pairingStatusMessage = string.Empty;

    [ObservableProperty]
    private bool _isPairing;

    [ObservableProperty]
    private int _burstShotIndex;

    [ObservableProperty]
    private int _burstShotTotal;

    public ObservableCollection<BurstShotItem> BurstShots { get; } = [];

    public ObservableCollection<AssetRecord> Backgrounds { get; } = [];

    public AppSettings Settings => _settingsService.Current;

    public IReadOnlyList<CameraDeviceInfo> AvailableCameras { get; private set; } = [];

    public IReadOnlyList<PrinterInfo> AvailablePrinters { get; private set; } = [];

    public IReadOnlyList<string> AvailableReceiptPorts { get; private set; } = [];

    public MainViewModel(
        PhotoboothStateMachine fsm,
        PreviewPipelineService preview,
        PhotoCaptureService capture,
        AssetCatalogService assetCatalog,
        SettingsService settingsService,
        IPrintingService printingService,
        IReceiptPrinterService receiptPrinterService,
        ICameraProvider camera,
        BackgroundRemovalServiceFactory backgroundRemovalFactory,
        EventRepository eventRepository,
        AssetRepository assetRepository,
        PhotoRepository photoRepository,
        DeviceRepository deviceRepository,
        SyncQueueRepository syncQueueRepository,
        CloudApiClient cloudApiClient,
        DevicePairingService devicePairingService,
        AssetManifestSyncService assetManifestSyncService,
        CloudSyncWorker cloudSyncWorker,
        Dispatcher dispatcher,
        ILogger<MainViewModel> logger)
    {
        _fsm = fsm;
        _preview = preview;
        _capture = capture;
        _assetCatalog = assetCatalog;
        _settingsService = settingsService;
        _printingService = printingService;
        _receiptPrinterService = receiptPrinterService;
        _camera = camera;
        _backgroundRemovalFactory = backgroundRemovalFactory;
        _eventRepository = eventRepository;
        _assetRepository = assetRepository;
        _photoRepository = photoRepository;
        _deviceRepository = deviceRepository;
        _syncQueueRepository = syncQueueRepository;
        _cloudApiClient = cloudApiClient;
        _devicePairingService = devicePairingService;
        _assetManifestSyncService = assetManifestSyncService;
        _cloudSyncWorker = cloudSyncWorker;
        _dispatcher = dispatcher;
        _logger = logger;

        _fsm.StateChanged += (_, e) => State = e.To;
        _preview.FrameReady += (_, bitmap) => PreviewImage = bitmap;
    }

    public async Task InitializeAsync()
    {
        await _settingsService.LoadAsync().ConfigureAwait(true);
        IsKioskMode = _settingsService.Current.Ui.KioskMode;

        await _assetCatalog.InitializeAsync().ConfigureAwait(true);
        Backgrounds.Clear();
        foreach (var background in _assetCatalog.Backgrounds)
        {
            Backgrounds.Add(background);
        }

        _activeEvent = await _eventRepository.GetActiveAsync().ConfigureAwait(true) ?? new EventDefinition
        {
            Id = "default",
            Name = "Default",
        };

        var template = OutputTemplate.BuiltIn.FirstOrDefault(t => t.Id == _activeEvent.OutputTemplateId)
            ?? OutputTemplate.DigitalLandscape;
        _preview.Template = template;

        AvailableCameras = _camera.ListDevices();
        AvailablePrinters = _printingService.ListPrinters();
        AvailableReceiptPorts = _receiptPrinterService.ListPorts();

        var cameraSettings = _settingsService.Current.Camera;
        var startOptions = new CameraStartOptions(
            cameraSettings.SelectedDeviceId,
            cameraSettings.RequestedWidth,
            cameraSettings.RequestedHeight,
            cameraSettings.RequestedFps);

        try
        {
            await _preview.StartAsync(startOptions).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start camera preview.");
            ErrorMessage = "Nepodařilo se spustit kameru: " + ex.Message;
            _fsm.TryFire(PhotoboothState.Error);
        }

        // Fire-and-forget: capture must never wait on the cloud (spec §35). Errors are logged and
        // swallowed inside the method itself.
        _ = StartCloudIfConfiguredAsync();
    }

    /// <summary>Starts the background sync worker and refreshes the asset manifest if this device
    /// is already paired (spec §35/§37/§38). A no-op until an admin completes pairing.</summary>
    private async Task StartCloudIfConfiguredAsync()
    {
        var cloud = _settingsService.Current.Cloud;
        if (!cloud.Enabled || string.IsNullOrWhiteSpace(cloud.DeviceToken))
        {
            return;
        }

        _cloudSyncWorker.Start();

        try
        {
            var config = await _cloudApiClient.GetConfigAsync(cloud.DeviceToken).ConfigureAwait(true);
            if (config.Event is not null)
            {
                await _assetManifestSyncService.SyncEventAssetsAsync(cloud.DeviceToken, config.Event.Id).ConfigureAwait(true);
                await RefreshBackgroundsFromRepositoryAsync().ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cloud config/asset sync failed at startup; continuing with local assets only.");
        }
    }

    /// <summary>Re-reads backgrounds from the database, which is what AssetManifestSyncService
    /// updates (spec §37) — unlike InitializeAsync's AssetCatalogService scan, this also picks up
    /// assets that came from the cloud rather than the bundled assets/backgrounds folder.</summary>
    private async Task RefreshBackgroundsFromRepositoryAsync()
    {
        var backgrounds = await _assetRepository.ListAsync(AssetType.Background).ConfigureAwait(true);
        Backgrounds.Clear();
        foreach (var background in backgrounds)
        {
            Backgrounds.Add(background);
        }
    }

    [RelayCommand]
    private void StartSession() => _fsm.TryFire(PhotoboothState.SelectingBackground);

    [RelayCommand]
    private void SelectBackground(AssetRecord asset)
    {
        SelectedBackground = asset;
        var loaded = AssetCatalogService.LoadImage(asset);
        var previous = _preview.SelectedBackground;
        _preview.SelectedBackground = loaded;
        previous?.Dispose();
        _fsm.TryFire(PhotoboothState.Preview);
    }

    [RelayCommand]
    private void BackToBackgroundSelection() => _fsm.TryFire(PhotoboothState.SelectingBackground);

    [RelayCommand]
    private async Task StartCountdownAsync()
    {
        if (!_fsm.TryFire(PhotoboothState.Countdown))
        {
            return;
        }

        _countdownCts = new CancellationTokenSource();
        var seconds = Math.Max(1, _settingsService.Current.Ui.CountdownSeconds);
        try
        {
            for (var i = seconds; i >= 1; i--)
            {
                CountdownValue = i;
                await Task.Delay(TimeSpan.FromSeconds(1), _countdownCts.Token).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await CaptureAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private void CancelCountdown()
    {
        _countdownCts?.Cancel();
        _fsm.TryFire(PhotoboothState.Preview);
    }

    private async Task CaptureAsync()
    {
        if (!_fsm.TryFire(PhotoboothState.Capturing))
        {
            return;
        }

        var burstCount = Math.Clamp(_settingsService.Current.Ui.BurstCount, 1, 9);
        var burstInterval = TimeSpan.FromMilliseconds(Math.Clamp(_settingsService.Current.Ui.BurstIntervalMs, 200, 10_000));
        BurstShotTotal = burstCount;
        BurstShotIndex = 0;

        try
        {
            // Scalar ObservableProperty updates are safe from the capture thread (WPF marshals
            // PropertyChanged for non-collection properties automatically).
            _burstFrames = await _capture.CaptureBurstAsync(burstCount, burstInterval, i => BurstShotIndex = i)
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Burst capture failed.");
            ErrorMessage = "Fotografování selhalo: " + ex.Message;
            _fsm.TryFire(PhotoboothState.Error);
            return;
        }

        if (burstCount == 1)
        {
            _fsm.TryFire(PhotoboothState.Processing);
            await ProcessSelectedFrameAsync(_burstFrames[0]).ConfigureAwait(true);
            return;
        }

        BurstShots.Clear();
        for (var i = 0; i < _burstFrames.Count; i++)
        {
            using var thumbnail = _burstFrames[i].Image.Clone(ctx => ctx.Resize(480, 0));
            BurstShots.Add(new BurstShotItem(i, ImageSharpWpfInterop.ToBitmapSource(thumbnail)));
        }

        _fsm.TryFire(PhotoboothState.SelectingPhoto);
        StartSelectPhotoTimeout();
    }

    /// <summary>The guest tapped one of the burst shots — process just that one; the rest of the
    /// burst is discarded when the selection flow ends (ProcessSelectedFrameAsync's finally).</summary>
    [RelayCommand]
    private async Task SelectBurstShotAsync(BurstShotItem shot)
    {
        _selectPhotoTimeoutCts?.Cancel();
        if (!_fsm.TryFire(PhotoboothState.Processing))
        {
            return;
        }

        if (_burstFrames is null || shot.Index >= _burstFrames.Count)
        {
            _fsm.TryFire(PhotoboothState.Error);
            return;
        }

        await ProcessSelectedFrameAsync(_burstFrames[shot.Index]).ConfigureAwait(true);
    }

    /// <summary>Retake from the selection screen: throw the whole burst away and go back to preview.</summary>
    [RelayCommand]
    private void RetakeBurst()
    {
        _selectPhotoTimeoutCts?.Cancel();
        DisposeBurstFrames();
        _fsm.TryFire(PhotoboothState.Preview);
    }

    private async Task ProcessSelectedFrameAsync(CameraFrame still)
    {
        Image<Rgba32>? blankBackground = null;
        try
        {
            var background = _preview.SelectedBackground ?? (blankBackground = new Image<Rgba32>(_preview.Template.WidthPx, _preview.Template.HeightPx));
            var record = await _capture.ProcessCapturedAsync(_activeEvent!.Id, still, background, _preview.SelectedOverlay, _preview.Template)
                .ConfigureAwait(true);

            ResultRecord = record;
            using var finalImage = Image.Load<Rgba32>(record.FinalPath);
            ResultImage = ImageSharpWpfInterop.ToBitmapSource(finalImage);
            QrCodeImage = null;
            // Cloud upload/QR (spec §41): never block the result screen on it — printing and the
            // next shoot must keep working while it syncs in the background. CloudSyncWorker already
            // picked this photo's SyncQueueItem up; we just poll for it to finish here.
            SyncStatusMessage = _settingsService.Current.Cloud.Enabled
                ? "Fotografie se synchronizuje..."
                : "Cloud synchronizace je vypnutá.";
            StartSyncStatusPolling(record.Id);

            _fsm.TryFire(PhotoboothState.Result);
            StartResultTimeout();

            if (_settingsService.Current.Print.AutoPrint)
            {
                await PrintAsync().ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Capture processing failed.");
            ErrorMessage = "Fotografování selhalo: " + ex.Message;
            _fsm.TryFire(PhotoboothState.Error);
        }
        finally
        {
            blankBackground?.Dispose();
            DisposeBurstFrames();
        }
    }

    private void DisposeBurstFrames()
    {
        if (_burstFrames is not null)
        {
            foreach (var frame in _burstFrames)
            {
                frame.Dispose();
            }

            _burstFrames = null;
        }

        BurstShots.Clear();
    }

    /// <summary>If nobody picks a shot (guest walked away mid-flow), discard the burst and reset —
    /// deliberately saving/printing nothing, so AutoPrint can't waste paper on an abandoned session.</summary>
    private void StartSelectPhotoTimeout()
    {
        _selectPhotoTimeoutCts?.Cancel();
        var cts = new CancellationTokenSource();
        _selectPhotoTimeoutCts = cts;
        var timeout = TimeSpan.FromSeconds(Math.Max(10, _settingsService.Current.Ui.ResultScreenTimeoutSeconds));

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(timeout, cts.Token).ConfigureAwait(false);
                await _dispatcher.InvokeAsync(() =>
                {
                    if (State == PhotoboothState.SelectingPhoto)
                    {
                        DisposeBurstFrames();
                        GoIdle();
                    }
                });
            }
            catch (OperationCanceledException)
            {
                // user picked a shot before the timeout — nothing to do
            }
        });
    }

    private void StartResultTimeout()
    {
        _resultTimeoutCts?.Cancel();
        var cts = new CancellationTokenSource();
        _resultTimeoutCts = cts;
        var timeout = TimeSpan.FromSeconds(Math.Max(3, _settingsService.Current.Ui.ResultScreenTimeoutSeconds));

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(timeout, cts.Token).ConfigureAwait(false);
                await _dispatcher.InvokeAsync(GoIdle);
            }
            catch (OperationCanceledException)
            {
                // user acted before the timeout — nothing to do
            }
        });
    }

    /// <summary>Polls until the just-captured photo shows up as synced, then renders its QR code
    /// (spec §40/§41). Cancelled as soon as the user leaves the Result screen.</summary>
    private void StartSyncStatusPolling(string photoId)
    {
        _syncPollCts?.Cancel();
        if (!_settingsService.Current.Cloud.Enabled)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _syncPollCts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), cts.Token).ConfigureAwait(false);
                    var photo = await _photoRepository.GetByIdAsync(photoId, cts.Token).ConfigureAwait(false);
                    if (photo is { Synced: true, DownloadUrl: not null })
                    {
                        var qrBytes = QrCodeService.GeneratePng(photo.DownloadUrl);
                        await _dispatcher.InvokeAsync(() =>
                        {
                            QrCodeImage = ImageSharpWpfInterop.FromEncodedBytes(qrBytes);
                            SyncStatusMessage = "Fotografie je online — naskenujte QR kód.";
                        });

                        var receiptSettings = _settingsService.Current.ReceiptPrinter;
                        if (receiptSettings is { Enabled: true, AutoPrintReceipt: true })
                        {
                            await PrintReceiptSlipAsync(photo.DownloadUrl).ConfigureAwait(false);
                        }

                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // left the Result screen before the upload finished — fine, sync continues in the background
            }
        }, cts.Token);
    }

    /// <summary>Prints the QR slip on the Bluetooth/serial thermal receipt printer, if configured
    /// (spec: BT POS printer add-on). Failure is soft — it only updates SyncStatusMessage, never the
    /// state machine, since a missing paper slip must not disturb an already-successful photo.</summary>
    private async Task PrintReceiptSlipAsync(string downloadUrl)
    {
        try
        {
            var modules = QrCodeService.GenerateModules(downloadUrl);
            var result = await _receiptPrinterService.PrintQrSlipAsync(modules, _settingsService.Current.ReceiptPrinter)
                .ConfigureAwait(false);
            if (!result.Success)
            {
                _logger.LogWarning("Receipt slip print failed: {Error}", result.ErrorMessage);
                await _dispatcher.InvokeAsync(() => { SyncStatusMessage = "Tisk účtenky selhal: " + result.ErrorMessage; })
                    .Task.ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Receipt slip print threw unexpectedly.");
        }
    }

    [RelayCommand]
    private async Task PrintReceiptAsync()
    {
        if (ResultRecord?.DownloadUrl is not { } downloadUrl)
        {
            SyncStatusMessage = "Účtenku lze vytisknout až po synchronizaci s cloudem.";
            return;
        }

        await PrintReceiptSlipAsync(downloadUrl).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task PrintAsync()
    {
        if (ResultRecord is null)
        {
            return;
        }

        _fsm.TryFire(PhotoboothState.Printing);
        try
        {
            var result = await _printingService.PrintAsync(ResultRecord.FinalPath, _settingsService.Current.Print)
                .ConfigureAwait(true);
            if (!result.Success)
            {
                // Spec §28/§46: a failed print must never lose the photo — just surface the error
                // and let the user retry from the (still fully intact) Result screen.
                ErrorMessage = "Tisk selhal: " + result.ErrorMessage;
            }
        }
        finally
        {
            _fsm.TryFire(PhotoboothState.Result);
        }
    }

    [RelayCommand]
    private void Retake()
    {
        _resultTimeoutCts?.Cancel();
        _syncPollCts?.Cancel();
        _fsm.TryFire(PhotoboothState.SelectingBackground);
    }

    [RelayCommand]
    private void Done()
    {
        _resultTimeoutCts?.Cancel();
        _syncPollCts?.Cancel();
        GoIdle();
    }

    private void GoIdle()
    {
        DisposeBurstFrames();
        SelectedBackground = null;
        ResultImage = null;
        ResultRecord = null;
        QrCodeImage = null;
        ErrorMessage = null;
        _fsm.Reset();
    }

    [RelayCommand]
    private void RequestAdmin()
    {
        PinInput = string.Empty;
        PinError = null;
        IsAdminUnlocked = false;
        _fsm.TryFire(PhotoboothState.Admin);
    }

    [RelayCommand]
    private void SubmitPin()
    {
        if (PinInput == _settingsService.Current.Ui.AdminPin)
        {
            PinError = null;
            IsAdminUnlocked = true;
            StartDiagnosticsTimer();
        }
        else
        {
            PinError = "Špatný PIN";
        }

        PinInput = string.Empty;
    }

    [RelayCommand]
    private async Task CloseAdminAsync()
    {
        IsAdminUnlocked = false;
        StopDiagnosticsTimer();
        await _settingsService.SaveAllAsync().ConfigureAwait(true);
        IsKioskMode = _settingsService.Current.Ui.KioskMode;
        _fsm.TryFire(PhotoboothState.Idle);
    }

    [RelayCommand]
    private void AcknowledgeError() => _fsm.Reset();

    /// <summary>F11 escape hatch for development — toggles the window's fullscreen presentation
    /// without touching the persisted Kiosk-mode admin setting (spec §20).</summary>
    [RelayCommand]
    private void ToggleFullscreen() => IsKioskMode = !IsKioskMode;

    /// <summary>Device pairing from the Admin > Cloud tab (spec §36): generates a code, shows it,
    /// then waits for an admin to confirm it on the backend before saving the resulting device
    /// identity and starting the sync worker.</summary>
    [RelayCommand]
    private async Task PairDeviceAsync()
    {
        if (IsPairing)
        {
            return;
        }

        IsPairing = true;
        PairingCode = null;
        PairingStatusMessage = "Zahajuji párování...";

        try
        {
            var code = await _devicePairingService.BeginAsync().ConfigureAwait(true);
            PairingCode = code;
            PairingStatusMessage = $"Zadejte kód {code} v administraci a potvrďte párování.";

            var result = await _devicePairingService.WaitForConfirmationAsync(code, TimeSpan.FromMinutes(10)).ConfigureAwait(true);
            switch (result.Outcome)
            {
                case PairingOutcome.Confirmed:
                    var device = await _deviceRepository.GetCurrentAsync().ConfigureAwait(true);
                    _settingsService.Current.Cloud.DeviceId = device?.DeviceId;
                    _settingsService.Current.Cloud.DeviceToken = device?.DeviceToken;
                    await _settingsService.SaveSectionAsync(nameof(AppSettings.Cloud), _settingsService.Current.Cloud)
                        .ConfigureAwait(true);
                    PairingStatusMessage = "Zařízení úspěšně spárováno.";
                    _ = StartCloudIfConfiguredAsync();
                    break;
                case PairingOutcome.Expired:
                    PairingStatusMessage = "Párovací kód vypršel, zkuste to znovu.";
                    break;
                case PairingOutcome.TimedOut:
                    PairingStatusMessage = "Časový limit pro párování vypršel.";
                    break;
                default:
                    PairingStatusMessage = "Párování zrušeno.";
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Device pairing failed.");
            PairingStatusMessage = "Chyba párování: " + ex.Message;
        }
        finally
        {
            IsPairing = false;
        }
    }
}
