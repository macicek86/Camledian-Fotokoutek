using System.Diagnostics;
using Camledian.Photobooth.Core.Models;
using Camledian.Photobooth.Imaging;
using Camledian.Photobooth.Imaging.PixelMath;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Camledian.Photobooth.AI;

/// <summary>
/// Local ONNX-based person/subject segmentation (spec §22/§23/§24/§25). Ships against the base
/// (CPU, cross-platform) Microsoft.ML.OnnxRuntime package so this actually runs — including in this
/// Linux devcontainer, given a model file — rather than only compiling. DirectML is attempted
/// opportunistically on Windows and falls back to CPU if unavailable; see <see cref="TryEnableDirectMl"/>.
///
/// Uses two separate models by design: a small/fast one for the live preview loop
/// (<see cref="AiSettings.PreviewModelPath"/>) and a larger, more accurate one for the one-shot final
/// render after capture (<see cref="AiSettings.FinalModelPath"/>) — preview needs to keep up with the
/// camera, final quality doesn't need to be fast since it only runs once per photo.
/// </summary>
public class AiBackgroundRemovalProvider : IBackgroundRemovalService, IDisposable
{
    private readonly Func<AiSettings> _getSettings;
    private readonly ILogger<AiBackgroundRemovalProvider> _logger;
    private readonly Lock _sessionGate = new();
    private readonly Dictionary<string, InferenceSession> _sessionsByPath = new();

    private float[]? _lastMask;
    private int _lastMaskWidth;
    private int _lastMaskHeight;
    private DateTime _lastInferenceUtc = DateTime.MinValue;
    private bool _loggedInputSizeOverride;

    public AiBackgroundRemovalProvider(Func<AiSettings> getSettings, ILogger<AiBackgroundRemovalProvider> logger)
    {
        _getSettings = getSettings;
        _logger = logger;
    }

    public string Name => "AI (ONNX)";

    /// <summary>Milliseconds the most recent inference call actually took — surfaced on the
    /// Diagnostics screen (spec §23/§44).</summary>
    public double LastInferenceMs { get; private set; }

    /// <summary>Which model actually served the most recent inference — useful on the Diagnostics
    /// tab to confirm the final render really did use the heavier model, not a silent fallback.</summary>
    public string? LastModelPathUsed { get; private set; }

    /// <summary>True when a final-quality render had to fall back to the small preview model because
    /// the heavier one isn't on disk. That fallback costs real quality — the preview model chops
    /// hands and arms off, and on a frame with a bright prop (a sparkler, a lit sign) it will happily
    /// decide the prop is the subject and cut the person away — so it must be visible, not just
    /// logged at Debug.</summary>
    public bool LastRenderFellBackToPreviewModel { get; private set; }

    /// <summary>True when the most recent inference produced an almost flat saliency map, i.e. the
    /// model separated nothing. Combiners use this to drop the AI's vote instead of letting a mask
    /// made of amplified noise win.</summary>
    public bool LastMaskWasLowConfidence { get; private set; }

    public Task<float[]> ApplyAsync(Image<Rgba32> frame, BackgroundRemovalOptions options, CancellationToken cancellationToken = default)
    {
        var settings = _getSettings();
        var resolutionChanged = _lastMask is null || _lastMaskWidth != frame.Width || _lastMaskHeight != frame.Height;
        var throttleElapsedMs = (DateTime.UtcNow - _lastInferenceUtc).TotalMilliseconds;
        var minIntervalMs = settings.PreviewInferenceFps > 0 ? 1000.0 / settings.PreviewInferenceFps : 0;

        // Only the live camera loop may reuse a mask (spec §24: camera at 30 FPS, AI at ~10-15 FPS).
        // Anything holding a distinct still — the final render, a burst thumbnail — asks for a fresh
        // one, independently of which model it wants; reusing a mask computed from a *different*
        // frame is not an optimisation, it is a wrong cutout.
        var mustRunInference = options.ForceFreshMask || resolutionChanged || throttleElapsedMs >= minIntervalMs;

        float[] mask;
        if (mustRunInference)
        {
            var sw = Stopwatch.StartNew();
            mask = RunInference(frame, settings, options.UseFinalQualityModel);
            sw.Stop();
            LastInferenceMs = sw.Elapsed.TotalMilliseconds;
            _logger.LogDebug(
                "AI inference ({Quality}) took {Ms:0.0} ms for a {W}x{H} frame using '{Model}'.",
                options.UseFinalQualityModel ? "final" : "preview", LastInferenceMs, frame.Width, frame.Height, LastModelPathUsed);

            _lastMask = mask;
            _lastMaskWidth = frame.Width;
            _lastMaskHeight = frame.Height;
            _lastInferenceUtc = DateTime.UtcNow;
        }
        else
        {
            mask = _lastMask!;
        }

        ApplyMaskToFrame(frame, mask, settings);
        return Task.FromResult(mask);
    }

    private float[] RunInference(Image<Rgba32> frame, AiSettings settings, bool useFinalQualityModel)
    {
        var session = GetOrCreateSession(settings, useFinalQualityModel);
        var inputName = session.InputMetadata.Keys.First();
        var inputSize = ResolveInputSize(session, inputName, settings);

        using var resized = frame.Clone(ctx => ctx.Resize(inputSize, inputSize));
        var inputTensor = AiPreprocessing.ToNormalizedTensor(resized);

        // U-2-Net exports seven outputs (the fused d0 plus six side outputs from progressively
        // coarser decoder stages). The fused one is first, which is what rembg uses too.
        var outputName = session.OutputMetadata.Keys.First();
        using var results = session.Run(
            [NamedOnnxValue.CreateFromTensor(inputName, inputTensor)],
            [outputName]);

        var outputTensor = results.First().AsTensor<float>();
        var rawMask = AiPreprocessing.ExtractAndNormalizeMask(outputTensor, inputSize, inputSize, out var lowConfidence);
        LastMaskWasLowConfidence = lowConfidence;
        if (lowConfidence)
        {
            _logger.LogWarning(
                "AI inference produced an almost flat saliency map for a {W}x{H} frame — the model found nothing " +
                "it could separate (typically a dark, noisy or heavily backlit frame). Background removal is being " +
                "skipped for this frame rather than keying it on noise.", frame.Width, frame.Height);
        }

        var upscaled = AiPreprocessing.ResizeMask(rawMask, inputSize, inputSize, frame.Width, frame.Height);

        var featherRadius = useFinalQualityModel ? settings.FeatherPixels : settings.FeatherPixels / 2.0;
        BoxBlur.Apply(upscaled, frame.Width, frame.Height, featherRadius);
        return upscaled;
    }

    private void ApplyMaskToFrame(Image<Rgba32> frame, float[] mask, AiSettings settings)
    {
        // A mask the model had no confidence in would dissolve the guests into the background. A
        // photo that still has the room in it is a far better failure than a photo of nobody, so
        // leave the frame untouched and let the operator see something is wrong.
        if (LastMaskWasLowConfidence)
        {
            return;
        }

        // The model output is already a continuous 0-1 saliency confidence, so it's used directly as
        // alpha; PreviewMaskThreshold only zeroes out low-confidence noise instead of letting faint
        // background specks show up as a ghosting halo.
        var cutoff = settings.PreviewMaskThreshold * 0.3f;

        frame.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < frame.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < frame.Width; x++)
                {
                    var idx = (y * frame.Width) + x;
                    var alpha = mask[idx] < cutoff ? 0f : mask[idx];
                    ref var px = ref row[x];
                    px.A = (byte)Math.Clamp((int)Math.Round(alpha * 255.0), 0, 255);
                }
            }
        });
    }

    /// <summary>
    /// The square size to feed the model. Most exports — including both U-2-Net files this app ships
    /// with — declare a fixed input shape (1x3x320x320), and handing such a model anything else just
    /// fails at Run() time; the configured <see cref="AiSettings.InputSize"/> is therefore only
    /// honoured when the model actually has a dynamic axis. That keeps "I typed 512 into Admin" from
    /// breaking capture, while letting a swapped-in model with dynamic axes use the bigger size.
    /// </summary>
    private int ResolveInputSize(InferenceSession session, string inputName, AiSettings settings)
    {
        var dimensions = session.InputMetadata[inputName].Dimensions;
        // NCHW: a fixed spatial axis is a positive number, a dynamic one is -1.
        var declared = dimensions.Length >= 4 ? dimensions[^1] : -1;
        if (declared <= 0)
        {
            return settings.InputSize;
        }

        if (declared != settings.InputSize && !_loggedInputSizeOverride)
        {
            _loggedInputSizeOverride = true;
            _logger.LogInformation(
                "AI model '{Model}' declares a fixed {Declared}x{Declared} input; ignoring the configured " +
                "InputSize of {Configured}.", LastModelPathUsed, declared, settings.InputSize);
        }

        return declared;
    }

    /// <summary>Picks the preview or final model per <paramref name="useFinalQualityModel"/>, falling back to
    /// the preview model if the final one isn't present — a missing "nice to have" heavier model
    /// degrades quality, it must never fail the whole capture (spec §46 in spirit).</summary>
    private InferenceSession GetOrCreateSession(AiSettings settings, bool useFinalQualityModel)
    {
        var previewPath = ResolveModelPath(settings.PreviewModelPath);
        var finalPath = ResolveModelPath(settings.FinalModelPath);

        var wantedPath = useFinalQualityModel && File.Exists(finalPath) ? finalPath : previewPath;
        LastRenderFellBackToPreviewModel = useFinalQualityModel && wantedPath == previewPath && finalPath != previewPath;
        if (LastRenderFellBackToPreviewModel)
        {
            _logger.LogWarning(
                "Final AI model '{FinalPath}' not found — this photo is being keyed with the small preview model, " +
                "which cuts hands, arms and props much more aggressively. Download it on the Admin screen.", finalPath);
        }

        lock (_sessionGate)
        {
            if (_sessionsByPath.TryGetValue(wantedPath, out var existing))
            {
                LastModelPathUsed = wantedPath;
                return existing;
            }

            if (!File.Exists(wantedPath))
            {
                throw new FileNotFoundException(
                    $"AI model not found at '{wantedPath}'. Run scripts/download-models.ps1 to fetch it " +
                    "(models are intentionally not committed to git — spec §22).",
                    wantedPath);
            }

            var options = new SessionOptions();
            if (settings.PreferDirectML)
            {
                TryEnableDirectMl(options);
            }

            var sw = Stopwatch.StartNew();
            var session = new InferenceSession(wantedPath, options);
            _sessionsByPath[wantedPath] = session;
            LastModelPathUsed = wantedPath;
            _logger.LogInformation("Loaded AI model '{ModelPath}' in {Ms:0} ms.", wantedPath, sw.Elapsed.TotalMilliseconds);
            return session;
        }
    }

    /// <summary>
    /// Best-effort DirectML activation. The base Microsoft.ML.OnnxRuntime package referenced by this
    /// project ships CPU-only native binaries (deliberately, so AI inference also runs on Linux/macOS
    /// and in this devcontainer) — DirectML's native EP is only present in the
    /// Microsoft.ML.OnnxRuntime.DirectML package. Appending the DML provider against the CPU-only
    /// native library will fail; we catch that and silently keep the (always present) CPU provider.
    /// To get real DirectML GPU acceleration on a Windows kiosk, swap the PackageReference in
    /// Camledian.Photobooth.AI.csproj for Microsoft.ML.OnnxRuntime.DirectML (Windows-only package).
    /// </summary>
    private void TryEnableDirectMl(SessionOptions options)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            options.AppendExecutionProvider_DML(0);
            _logger.LogInformation("AI: DirectML execution provider enabled.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "AI: DirectML requested but unavailable in this build; using CPU. " +
                "Swap to Microsoft.ML.OnnxRuntime.DirectML for GPU acceleration.");
        }
    }

    /// <summary>True as soon as the preview model exists — that's the minimum bar for AI/Hybrid mode
    /// to work at all; the final model is a "nicer if present" upgrade handled by the fallback above.</summary>
    public bool IsPreviewModelAvailable(AiSettings settings) => File.Exists(ResolveModelPath(settings.PreviewModelPath));

    private static string ResolveModelPath(string configuredPath) => AiModelCatalog.ResolvePath(configuredPath);

    public void Dispose()
    {
        lock (_sessionGate)
        {
            foreach (var session in _sessionsByPath.Values)
            {
                session.Dispose();
            }

            _sessionsByPath.Clear();
        }
    }
}
