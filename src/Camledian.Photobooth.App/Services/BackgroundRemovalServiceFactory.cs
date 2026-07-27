using Camledian.Photobooth.AI;
using Camledian.Photobooth.Core.Models;
using Camledian.Photobooth.Imaging;
using Camledian.Photobooth.Imaging.BackgroundSubtraction;
using Camledian.Photobooth.Imaging.ChromaKey;
using Microsoft.Extensions.Logging;

namespace Camledian.Photobooth.App.Services;

/// <summary>
/// Resolves the active <see cref="IBackgroundRemovalService"/> from the live
/// <see cref="Core.Models.UiSettings.BackgroundRemovalMode"/> setting (spec §27) — called fresh on
/// every preview frame and every capture so an admin's mode change takes effect immediately, the
/// same way chroma-key slider edits do.
/// </summary>
public class BackgroundRemovalServiceFactory
{
    private readonly SettingsService _settingsService;
    private readonly AiBackgroundRemovalProvider _aiProvider;
    private readonly GreenScreenBackgroundRemovalService _greenScreen;
    private readonly HybridBackgroundRemovalProvider _hybrid;
    private readonly BackgroundSubtractionRemovalService _backgroundSubtraction;
    private readonly BackgroundSubtractionAiHybridProvider _backgroundSubtractionHybrid;
    private readonly ILogger<BackgroundRemovalServiceFactory> _logger;
    private bool _missingModelWarningLogged;
    private bool _missingReferenceWarningLogged;

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
        _backgroundSubtraction = new BackgroundSubtractionRemovalService(() => settingsService.Current.BackgroundSubtraction);
        _backgroundSubtractionHybrid = new BackgroundSubtractionAiHybridProvider(_backgroundSubtraction, aiProvider);
    }

    /// <summary>Set whenever Resolve() had to substitute Green Screen for another mode because a
    /// prerequisite is missing (AI model not downloaded, or no reference photo captured yet), so the
    /// UI can show a one-time notice (spec §46 in spirit: "upozornění, nabídnout Green Screen").</summary>
    public string? LastFallbackNotice { get; private set; }

    public IBackgroundRemovalService Resolve()
    {
        var mode = _settingsService.Current.Ui.BackgroundRemovalMode;
        var needsAi = mode is BackgroundRemovalMode.Ai or BackgroundRemovalMode.Hybrid
            or BackgroundRemovalMode.BackgroundSubtractionHybrid or BackgroundRemovalMode.Auto;
        var needsReference = mode is BackgroundRemovalMode.BackgroundSubtraction or BackgroundRemovalMode.BackgroundSubtractionHybrid;

        if (needsAi && !_aiProvider.IsPreviewModelAvailable(_settingsService.Current.Ai))
        {
            if (!_missingModelWarningLogged)
            {
                _missingModelWarningLogged = true;
                _logger.LogWarning(
                    "AI preview model '{ModelPath}' not found; falling back to Green Screen. Run scripts/download-models.ps1.",
                    _settingsService.Current.Ai.PreviewModelPath);
            }

            LastFallbackNotice = "AI model nenalezen — použit Green Screen. Spusťte scripts/download-models.ps1.";
            return _greenScreen;
        }

        if (needsReference && !_backgroundSubtraction.HasReferenceImage(_settingsService.Current.BackgroundSubtraction))
        {
            if (!_missingReferenceWarningLogged)
            {
                _missingReferenceWarningLogged = true;
                _logger.LogWarning("No background-subtraction reference photo captured yet; falling back to Green Screen.");
            }

            LastFallbackNotice = "Nejdřív vyfoťte prázdné pozadí (Admin > Odečítání pozadí) — zatím používám Green Screen.";
            return _greenScreen;
        }

        // Every prerequisite is satisfied — clear any earlier notice and re-arm the one-time warnings.
        // Without this the "AI model nenalezen" banner stayed on the live screen forever, including
        // after an admin downloaded the model from Admin > AI / Hybrid and the mode started working.
        LastFallbackNotice = null;
        _missingModelWarningLogged = false;
        _missingReferenceWarningLogged = false;

        return mode switch
        {
            BackgroundRemovalMode.Ai => _aiProvider,
            BackgroundRemovalMode.Hybrid => _hybrid,
            BackgroundRemovalMode.BackgroundSubtraction => _backgroundSubtraction,
            BackgroundRemovalMode.BackgroundSubtractionHybrid => _backgroundSubtractionHybrid,
            // Auto is reserved for a future automatic heuristic (spec §27); Hybrid is the safest
            // stand-in since it degrades gracefully to whichever signal (chroma/AI) is actually usable.
            BackgroundRemovalMode.Auto => _hybrid,
            _ => _greenScreen,
        };
    }

    public AiBackgroundRemovalProvider AiProvider => _aiProvider;

    public HybridBackgroundRemovalProvider HybridProvider => _hybrid;

    public BackgroundSubtractionRemovalService BackgroundSubtractionProvider => _backgroundSubtraction;

    public BackgroundSubtractionAiHybridProvider BackgroundSubtractionHybridProvider => _backgroundSubtractionHybrid;
}
