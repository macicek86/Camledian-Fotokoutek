using System.IO;
using System.Linq;
using Camledian.Photobooth.AI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Camledian.Photobooth.App.ViewModels;

/// <summary>
/// Admin &gt; AI / Hybrid model management: shows which ONNX models are actually on disk and
/// downloads the missing ones in place. Without this the only way to get AI mode working was to run
/// scripts/download-models.ps1 and rebuild — not something an operator can do mid-event, and the
/// screen gave no hint that a download was even the missing step.
/// </summary>
public partial class MainViewModel
{
    private CancellationTokenSource? _aiModelDownloadCts;

    [ObservableProperty]
    private string _aiModelStatus = "-";

    /// <summary>Drives the "Stáhnout" button's visibility — there is nothing to offer when both
    /// models are already present, or when the admin repointed the paths at a model this app has no
    /// URL or checksum for.</summary>
    [ObservableProperty]
    private bool _canDownloadAiModels;

    [ObservableProperty]
    private bool _isAiModelDownloading;

    [ObservableProperty]
    private double _aiModelDownloadPercent;

    [ObservableProperty]
    private string _aiModelDownloadDetail = string.Empty;

    /// <summary>Recomputes the on-disk status of both configured models. Cheap (two File.Exists),
    /// so it runs whenever the Admin screen is unlocked and after every download.</summary>
    public void RefreshAiModelStatus()
    {
        if (IsAiModelDownloading)
        {
            return;
        }

        var missing = GetMissingModels();
        if (missing.Count == 0)
        {
            AiModelStatus = "Oba modely jsou staženy a připraveny.";
            CanDownloadAiModels = false;
            return;
        }

        var unknown = missing.Where(m => m.Descriptor is null).Select(m => m.ConfiguredPath).ToList();
        var known = missing.Where(m => m.Descriptor is not null).ToList();

        if (known.Count == 0)
        {
            AiModelStatus = "Chybí model na cestě " + string.Join(", ", unknown) +
                " — tento model není v katalogu aplikace, stáhněte ho ručně.";
            CanDownloadAiModels = false;
            return;
        }

        var totalMb = known.Sum(m => m.Descriptor!.ApproximateBytes) / 1_000_000.0;
        var names = string.Join(", ", known.Select(m => m.Descriptor!.FileName));
        // Spelling out what each missing model actually costs: the preview one is a hard blocker, the
        // final one degrades every photo silently, which is the more insidious of the two.
        var missingFinal = known.Any(m => m.Descriptor!.FileName == AiModelCatalog.FinalModel.FileName);
        var consequence = missingFinal
            ? "Bez finálního modelu se fotky klíčují malým náhledovým modelem — ořezává ruce, paže a rekvizity. " +
              "Bez modelu pro náhled se AI a Hybrid režim přepnou na Green Screen."
            : "Bez modelu pro náhled se AI a Hybrid režim přepnou na Green Screen.";
        AiModelStatus = $"Chybí: {names} (~{totalMb:0} MB). {consequence}";
        CanDownloadAiModels = true;
    }

    /// <summary>The configured preview/final models that are not on disk, paired with their catalog
    /// entry (null when the configured file name isn't one this app knows how to fetch).</summary>
    private List<(string ConfiguredPath, AiModelDescriptor? Descriptor)> GetMissingModels()
    {
        var ai = _settingsService.Current.Ai;
        var configured = new[] { ai.PreviewModelPath, ai.FinalModelPath }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return configured
            .Where(path => !File.Exists(AiModelCatalog.ResolvePath(path)))
            .Select(path => (path, AiModelCatalog.FindByConfiguredPath(path)))
            .ToList();
    }

    [RelayCommand]
    private async Task DownloadAiModelsAsync()
    {
        if (IsAiModelDownloading)
        {
            return;
        }

        var pending = GetMissingModels()
            .Where(m => m.Descriptor is not null)
            .Select(m => (m.ConfiguredPath, Descriptor: m.Descriptor!))
            .ToList();

        if (pending.Count == 0)
        {
            RefreshAiModelStatus();
            return;
        }

        _aiModelDownloadCts = new CancellationTokenSource();
        IsAiModelDownloading = true;
        CanDownloadAiModels = false;

        try
        {
            for (var i = 0; i < pending.Count; i++)
            {
                var (configuredPath, descriptor) = pending[i];
                var destination = AiModelCatalog.ResolvePath(configuredPath);
                var label = pending.Count > 1 ? $"{descriptor.FileName} ({i + 1}/{pending.Count})" : descriptor.FileName;

                AiModelStatus = $"Stahuji {label}...";
                AiModelDownloadPercent = 0;
                AiModelDownloadDetail = string.Empty;

                // Progress arrives on the download's thread; Report marshals it back so the bar and
                // its label update together rather than tearing across a frame.
                var progress = new Progress<AiModelDownloadProgress>(p =>
                {
                    AiModelDownloadPercent = p.TotalBytes > 0
                        ? Math.Clamp(p.BytesReceived * 100.0 / p.TotalBytes, 0, 100)
                        : 0;
                    AiModelDownloadDetail = p.Verifying
                        ? "Ověřuji kontrolní součet..."
                        : $"{p.BytesReceived / 1_000_000.0:0.0} / {p.TotalBytes / 1_000_000.0:0.0} MB";
                });

                await _aiModelDownloadService
                    .DownloadAsync(descriptor, destination, progress, _aiModelDownloadCts.Token)
                    .ConfigureAwait(true);
            }

            AiModelDownloadDetail = string.Empty;
            IsAiModelDownloading = false;
            RefreshAiModelStatus();

            // The provider re-checks the file on its next call, so the very next preview frame picks
            // the model up — no restart, matching how every other Admin setting behaves.
            AiModelStatus = "Hotovo — modely ověřeny. " + AiModelStatus;
        }
        catch (OperationCanceledException)
        {
            IsAiModelDownloading = false;
            RefreshAiModelStatus();
            AiModelStatus = "Stahování zrušeno. " + AiModelStatus;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI model download failed.");
            IsAiModelDownloading = false;
            RefreshAiModelStatus();
            AiModelStatus = "Stahování selhalo: " + ex.Message;
            CanDownloadAiModels = true;
        }
        finally
        {
            IsAiModelDownloading = false;
            AiModelDownloadPercent = 0;
            AiModelDownloadDetail = string.Empty;
            _aiModelDownloadCts?.Dispose();
            _aiModelDownloadCts = null;
        }
    }

    [RelayCommand]
    private void CancelAiModelDownload() => _aiModelDownloadCts?.Cancel();
}
