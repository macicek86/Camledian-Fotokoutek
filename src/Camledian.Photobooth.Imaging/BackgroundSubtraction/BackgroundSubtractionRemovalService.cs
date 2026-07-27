using Camledian.Photobooth.Core.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Camledian.Photobooth.Imaging.BackgroundSubtraction;

/// <summary>Adapts <see cref="BackgroundSubtractionProcessor"/> to <see cref="IBackgroundRemovalService"/>,
/// lazily loading and caching the reference photo from disk (reloading if the configured path
/// changes, e.g. after an admin re-captures it).</summary>
public class BackgroundSubtractionRemovalService(Func<BackgroundSubtractionSettings> getSettings) : IBackgroundRemovalService, IDisposable
{
    private readonly Lock _gate = new();
    private Image<Rgba32>? _referenceImage;
    private string? _loadedReferencePath;

    public string Name => "Background Subtraction";

    /// <summary>True once a reference photo has actually been captured and is loadable — the mode
    /// falls back to Green Screen until this is true (spec-inspired UX addition, mirrors the "AI
    /// model missing -> Green Screen" fallback in BackgroundRemovalServiceFactory).</summary>
    public bool HasReferenceImage(BackgroundSubtractionSettings settings) =>
        !string.IsNullOrWhiteSpace(settings.ReferenceImagePath) && File.Exists(ResolvePath(settings.ReferenceImagePath));

    /// <remarks>Ignores <paramref name="options"/>: the mask is recomputed against the reference
    /// photo every call, so there is no cached mask to bypass and no heavier model to pick.</remarks>
    public Task<float[]> ApplyAsync(Image<Rgba32> frame, BackgroundRemovalOptions options, CancellationToken cancellationToken = default)
    {
        var mask = TryComputeMask(frame)
            ?? throw new InvalidOperationException("No background-subtraction reference photo captured yet.");
        return Task.FromResult(mask);
    }

    /// <summary>Same as <see cref="ApplyAsync"/> but returns null instead of throwing when no
    /// reference photo has been captured yet — used by combiners (e.g. background-subtraction + AI)
    /// that need to check availability without exceptions as control flow.</summary>
    public float[]? TryComputeMask(Image<Rgba32> frame)
    {
        var settings = getSettings();
        var reference = GetOrLoadReference(settings);
        return reference is null ? null : BackgroundSubtractionProcessor.Apply(frame, reference, settings);
    }

    private Image<Rgba32>? GetOrLoadReference(BackgroundSubtractionSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ReferenceImagePath))
        {
            return null;
        }

        var fullPath = ResolvePath(settings.ReferenceImagePath);

        lock (_gate)
        {
            if (_referenceImage is not null && _loadedReferencePath == fullPath)
            {
                return _referenceImage;
            }

            if (!File.Exists(fullPath))
            {
                return null;
            }

            _referenceImage?.Dispose();
            _referenceImage = Image.Load<Rgba32>(fullPath);
            _loadedReferencePath = fullPath;
            return _referenceImage;
        }
    }

    private static string ResolvePath(string configuredPath) =>
        Path.IsPathRooted(configuredPath) ? configuredPath : Path.Combine(AppContext.BaseDirectory, configuredPath);

    public void Dispose()
    {
        lock (_gate)
        {
            _referenceImage?.Dispose();
            _referenceImage = null;
        }
    }
}
