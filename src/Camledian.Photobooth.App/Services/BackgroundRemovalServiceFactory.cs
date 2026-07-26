using System.IO;
using Camledian.Photobooth.AI;
using Camledian.Photobooth.Core.Models;
using Camledian.Photobooth.Imaging;
using Camledian.Photobooth.Imaging.ChromaKey;
using Microsoft.Extensions.Logging;

namespace Camledian.Photobooth.App.Services;

/// <summary>
/// Resolves the active <see cref="IBackgroundRemovalService"/> from the live
/// <see cref="Core.Models.UiSettings.BackgroundRemovalMode"/> setting (spec §27) — called fresh on
/// every preview frame and every capture so an admin's mode change (Green Screen / AI / Hybrid)
/// takes effect immediately, the same way chroma-key slider edits do.
/// </summary>
public class BackgroundRemovalServiceFactory
{
    private readonly SettingsService _settingsService;
    private readonly AiBackgroundRemovalProvider _aiProvider;
    private readonly GreenScreenBackgroundRemovalService _greenScreen;
    private readonly HybridBackgroundRemovalProvider _hybrid;
    private readonly ILogger<BackgroundRemovalServiceFactory> _logger;
    private bool _missingModelWarningLogged;

    public BackgroundRemovalServiceFactory(
        SettingsService settingsService,
        AiBackgroundRemovalProvider aiProvider,
        ILogger<BackgroundRemovalServiceFactory> logger)
    {
        _settingsService = settingsService;
        _aiProvider = aiProvider;
        _logger = logger;
        _greenScreen = new GreenScreenBackgroundRemovalService(() => settingsService.Current.ChromaKey);
        _hybrid = new HybridBackgroundRemovalProvider(() => settingsService.Current.ChromaKey, aiProvider);
    }

    /// <summary>Set whenever Resolve() had to substitute Green Screen for AI/Hybrid because the ONNX
    /// model file isn't present, so the UI can show a one-time notice (spec §46: "AI model není:
    /// upozornění, nabídnout Green Screen").</summary>
    public string? LastFallbackNotice { get; private set; }

    public IBackgroundRemovalService Resolve()
    {
        var mode = _settingsService.Current.Ui.BackgroundRemovalMode;
        var needsAi = mode is BackgroundRemovalMode.Ai or BackgroundRemovalMode.Hybrid or BackgroundRemovalMode.Auto;

        if (needsAi && !IsModelAvailable())
        {
            if (!_missingModelWarningLogged)
            {
                _missingModelWarningLogged = true;
                _logger.LogWarning(
                    "AI model '{ModelPath}' not found; falling back to Green Screen. Run scripts/download-models.ps1.",
                    _settingsService.Current.Ai.ModelPath);
            }

            LastFallbackNotice = "AI model nenalezen — použit Green Screen. Spusťte scripts/download-models.ps1.";
            return _greenScreen;
        }

        return mode switch
        {
            BackgroundRemovalMode.Ai => _aiProvider,
            BackgroundRemovalMode.Hybrid => _hybrid,
            // Auto is reserved for a future automatic heuristic (spec §27); Hybrid is the safest
            // stand-in since it degrades gracefully to whichever signal (chroma/AI) is actually usable.
            BackgroundRemovalMode.Auto => _hybrid,
            _ => _greenScreen,
        };
    }

    private bool IsModelAvailable()
    {
        var configuredPath = _settingsService.Current.Ai.ModelPath;
        var fullPath = Path.IsPathRooted(configuredPath) ? configuredPath : Path.Combine(AppContext.BaseDirectory, configuredPath);
        return File.Exists(fullPath);
    }

    public AiBackgroundRemovalProvider AiProvider => _aiProvider;

    public HybridBackgroundRemovalProvider HybridProvider => _hybrid;
}
