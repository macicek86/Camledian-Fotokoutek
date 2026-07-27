using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Camledian.Photobooth.AI;
using Camledian.Photobooth.App.Bootstrap;
using Camledian.Photobooth.App.Diagnostics;
using Camledian.Photobooth.App.Services;
using Camledian.Photobooth.App.ViewModels;
using Camledian.Photobooth.Camera;
using Camledian.Photobooth.Cloud;
using Camledian.Photobooth.Cloud.Services;
using Camledian.Photobooth.Core.Models;
using Camledian.Photobooth.Core.StateMachine;
using Camledian.Photobooth.Imaging.Composition;
using Camledian.Photobooth.Printing;
using Camledian.Photobooth.Storage;
using Camledian.Photobooth.Storage.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Wpf.Ui.Appearance;

namespace Camledian.Photobooth.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ApplyBrandAccent();

        var builder = Host.CreateApplicationBuilder();
        builder.Configuration
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
            .AddEnvironmentVariables("CAMLEDIAN_");

        var environmentName = builder.Configuration["Environment"] ?? "Kiosk";
        var appEnvironment = Enum.TryParse<AppEnvironment>(environmentName, ignoreCase: true, out var parsed)
            ? parsed
            : AppEnvironment.Kiosk;

        ConfigureLogging(builder, appEnvironment);
        ConfigureServices(builder.Services, appEnvironment);

        _host = builder.Build();

        // Bootstrap the database/data directories with conservative defaults *before* admin-editable
        // settings are loaded from it (spec §17: DB must initialize itself with no manual step).
        var bootstrapStorageSettings = new StorageSettings();
        StorageInitializer.Initialize(bootstrapStorageSettings, _host.Services.GetRequiredService<SqliteConnectionFactory>());

        var settingsService = _host.Services.GetRequiredService<SettingsService>();
        await settingsService.LoadAsync().ConfigureAwait(true);

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();

        var viewModel = (MainViewModel)mainWindow.DataContext!;
        await viewModel.InitializeAsync().ConfigureAwait(true);

        // CI-only screenshot pass (see UiCaptureRunner). Deliberately after InitializeAsync so the
        // captures show real loaded settings and the real background catalogue, not empty defaults.
        if (TryGetCaptureDirectory(e.Args, out var captureDirectory))
        {
            await UiCaptureRunner.RunAsync(mainWindow, viewModel, captureDirectory).ConfigureAwait(true);
            Shutdown();
        }
    }

    /// <summary>
    /// Reads <c>--ui-capture &lt;dir&gt;</c> off the command line. The kiosk is launched with no
    /// arguments, so this stays dormant in normal operation.
    /// </summary>
    private static bool TryGetCaptureDirectory(string[] args, out string directory)
    {
        var index = Array.IndexOf(args, UiCaptureRunner.Flag);
        if (index >= 0 && index + 1 < args.Length)
        {
            directory = args[index + 1];
            return true;
        }

        directory = string.Empty;
        return false;
    }

    /// <summary>
    /// Repoints WPF UI's accent resources at the brand colour. The theme itself is declared in App.xaml
    /// (<c>ui:ThemesDictionary Theme="Dark"</c>); only the accent needs code, because WPF UI ships a
    /// Windows-blue default and derives the lighter/darker shades used by focus rings, slider tracks and
    /// selected list items from it. Deliberately not routed through
    /// <c>ApplicationThemeManager.Apply</c>, whose <c>updateAccent</c> would overwrite this with the
    /// machine's Windows accent colour — a kiosk must look the same on every machine it is deployed to.
    /// </summary>
    private static void ApplyBrandAccent()
    {
        if (Current.TryFindResource("BrandAccentColor") is Color brandAccent)
        {
            ApplicationAccentColorManager.Apply(brandAccent, ApplicationTheme.Dark);
        }
    }

    private static void ConfigureLogging(HostApplicationBuilder builder, AppEnvironment environment)
    {
        var logsDirectory = StoragePaths.Resolve(new StorageSettings().LogsDirectory);
        Directory.CreateDirectory(logsDirectory);

        var loggerConfig = new LoggerConfiguration()
            .MinimumLevel.Is(environment == AppEnvironment.Development
                ? Serilog.Events.LogEventLevel.Debug
                : Serilog.Events.LogEventLevel.Information)
            .WriteTo.File(
                Path.Combine(logsDirectory, "photobooth-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30);

        if (environment == AppEnvironment.Development)
        {
            loggerConfig = loggerConfig.WriteTo.Debug();
        }

        Log.Logger = loggerConfig.CreateLogger();
        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(Log.Logger, dispose: true);
    }

    private static void ConfigureServices(IServiceCollection services, AppEnvironment environment)
    {
        // Storage
        services.AddSingleton(_ => new SqliteConnectionFactory(new StorageSettings().DatabaseFile));
        services.AddSingleton<SettingsRepository>();
        services.AddSingleton<EventRepository>();
        services.AddSingleton<AssetRepository>();
        services.AddSingleton<PhotoRepository>();
        services.AddSingleton<SyncQueueRepository>();
        services.AddSingleton<DeviceRepository>();
        services.AddSingleton(sp => new PhotoFileStore(sp.GetRequiredService<SettingsService>().Current.Storage));

        // Settings / assets
        services.AddSingleton<SettingsService>();
        services.AddSingleton<AssetCatalogService>();

        // Core
        services.AddSingleton<PhotoboothStateMachine>();

        // Imaging
        services.AddSingleton<ImageCompositionService>();

        // AI / Hybrid background removal (spec §22/§26/§27)
        services.AddSingleton(sp => new AiBackgroundRemovalProvider(
            () => sp.GetRequiredService<SettingsService>().Current.Ai,
            sp.GetRequiredService<ILogger<AiBackgroundRemovalProvider>>()));
        services.AddSingleton<BackgroundRemovalServiceFactory>();
        // Its own HttpClient: model downloads are hundreds of MB and must not share a timeout or
        // connection budget with the cloud sync client.
        services.AddSingleton(sp => new AiModelDownloadService(
            new HttpClient { Timeout = Timeout.InfiniteTimeSpan },
            sp.GetRequiredService<ILogger<AiModelDownloadService>>()));

        // Camera
        services.AddSingleton<CameraProviderFactory>();
        services.AddSingleton<ICameraProvider>(sp =>
            sp.GetRequiredService<CameraProviderFactory>().Create(sp.GetRequiredService<SettingsService>().Current.Camera));

        // Printing
        services.AddSingleton<IPrintingService, WindowsPrintingService>();
        services.AddSingleton<IReceiptPrinterService, SerialReceiptPrinterService>();

        // Cloud (spec §35/§36/§37/§38/§40): the base URL is re-read from settings on every request
        // (see CloudApiClient.BuildUri) so an admin editing it in the Cloud tab takes effect
        // immediately — no app restart needed, same as every other live-editable setting.
        services.AddSingleton(sp => new CloudApiClient(
            new HttpClient(),
            () => sp.GetRequiredService<SettingsService>().Current.Cloud.ApiBaseUrl));
        services.AddSingleton<DevicePairingService>();
        services.AddSingleton(sp => new AssetManifestSyncService(
            sp.GetRequiredService<CloudApiClient>(),
            sp.GetRequiredService<AssetRepository>(),
            sp.GetRequiredService<SettingsService>().Current.Storage,
            new HttpClient(),
            sp.GetRequiredService<ILogger<AssetManifestSyncService>>()));
        services.AddSingleton(sp => new CloudSyncWorker(
            sp.GetRequiredService<CloudApiClient>(),
            sp.GetRequiredService<PhotoRepository>(),
            sp.GetRequiredService<SyncQueueRepository>(),
            () => sp.GetRequiredService<SettingsService>().Current.Cloud,
            sp.GetRequiredService<ILogger<CloudSyncWorker>>()));

        // App services
        services.AddSingleton(sp => Dispatcher.CurrentDispatcher);
        services.AddSingleton<PreviewPipelineService>();
        services.AddSingleton<PhotoCaptureService>();

        // UI
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
