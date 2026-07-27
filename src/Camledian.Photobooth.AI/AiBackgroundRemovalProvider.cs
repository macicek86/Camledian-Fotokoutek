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

    public Task<float[]> ApplyAsync(Image<Rgba32> frame, bool highQuality, CancellationToken cancellationToken = default)
    {
        var settings = _getSettings();
        var resolutionChanged = _lastMask is null || _lastMaskWidth != frame.Width || _lastMaskHeight != frame.Height;
        var throttleElapsedMs = (DateTime.UtcNow - _lastInferenceUtc).TotalMilliseconds;
        var minIntervalMs = settings.PreviewInferenceFps > 0 ? 1000.0 / settings.PreviewInferenceFps : 0;

        // Preview can reuse the last known mask between inference runs (spec §24: camera at 30 FPS,
        // AI at ~10-15 FPS); final capture always runs a fresh, full-resolution pass (spec §25).
        var mustRunInference = highQuality || resolutionChanged || throttleElapsedMs >= minIntervalMs;

        float[] mask;
        if (mustRunInference)
        {
            var sw = Stopwatch.StartNew();
            mask = RunInference(frame, settings, highQuality);
            sw.Stop();
            LastInferenceMs = sw.Elapsed.TotalMilliseconds;
            _logger.LogDebug(
                "AI inference ({Quality}) took {Ms:0.0} ms for a {W}x{H} frame using '{Model}'.",
                highQuality ? "final" : "preview", LastInferenceMs, frame.Width, frame.Height, LastModelPathUsed);

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

    private float[] RunInference(Image<Rgba32> frame, AiSettings settings, bool highQuality)
    {
        var session = GetOrCreateSession(settings, highQuality);
        var inputSize = settings.InputSize;

        using var resized = frame.Clone(ctx => ctx.Resize(inputSize, inputSize));
        var inputTensor = AiPreprocessing.ToNormalizedTensor(resized);

        var inputName = session.InputMetadata.Keys.First();
        var outputName = session.OutputMetadata.Keys.First();
        using var results = session.Run(
            [NamedOnnxValue.CreateFromTensor(inputName, inputTensor)],
            [outputName]);

        var outputTensor = results.First().AsTensor<float>();
        var rawMask = AiPreprocessing.ExtractAndNormalizeMask(outputTensor, inputSize, inputSize);

        var upscaled = AiPreprocessing.ResizeMask(rawMask, inputSize, inputSize, frame.Width, frame.Height);

        var featherRadius = highQuality ? settings.FeatherPixels : settings.FeatherPixels / 2.0;
        BoxBlur.Apply(upscaled, frame.Width, frame.Height, featherRadius);
        return upscaled;
    }

    private void ApplyMaskToFrame(Image<Rgba32> frame, float[] mask, AiSettings settings)
    {
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

    /// <summary>Picks the preview or final model per <paramref name="highQuality"/>, falling back to
    /// the preview model if the final one isn't present — a missing "nice to have" heavier model
    /// degrades quality, it must never fail the whole capture (spec §46 in spirit).</summary>
    private InferenceSession GetOrCreateSession(AiSettings settings, bool highQuality)
    {
        var previewPath = ResolveModelPath(settings.PreviewModelPath);
        var finalPath = ResolveModelPath(settings.FinalModelPath);

        var wantedPath = highQuality && File.Exists(finalPath) ? finalPath : previewPath;
        if (highQuality && wantedPath == previewPath && finalPath != previewPath)
        {
            _logger.LogDebug("Final AI model '{FinalPath}' not found; using the preview model instead.", finalPath);
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
