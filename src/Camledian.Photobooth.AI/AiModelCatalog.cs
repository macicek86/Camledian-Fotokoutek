namespace Camledian.Photobooth.AI;

/// <summary>One downloadable ONNX model: where it comes from and what it must hash to.</summary>
/// <param name="FileName">File name as referenced from <see cref="Core.Models.AiSettings"/>.</param>
/// <param name="Url">Direct download URL.</param>
/// <param name="Sha256">Expected SHA-256 of the complete file, lowercase hex. This is what makes a
/// truncated or corrupted download detectable instead of silently landing on disk as a "model".</param>
/// <param name="ApproximateBytes">Rough size, used to show something sensible in the UI before the
/// server's Content-Length is known.</param>
/// <param name="Required">False for the heavier final-quality model, which the app transparently
/// substitutes with the preview model when absent.</param>
public sealed record AiModelDescriptor(
    string FileName,
    string Url,
    string Sha256,
    long ApproximateBytes,
    bool Required);

/// <summary>
/// The models <c>scripts/download-models.ps1</c> fetches, mirrored in code so the Admin screen can
/// download them itself — the script is a convenience, not a prerequisite an operator at an event
/// can be expected to run. Both are Apache-2.0 U-2-Net exports published by the rembg project.
/// Keep the hashes here and in the script in sync.
/// </summary>
public static class AiModelCatalog
{
    public static AiModelDescriptor PreviewModel { get; } = new(
        "u2netp.onnx",
        "https://github.com/danielgatis/rembg/releases/download/v0.0.0/u2netp.onnx",
        "309c8469258dda742793dce0ebea8e6dd393174f89934733ecc8b14c76f4ddd8",
        4_574_861,
        Required: true);

    public static AiModelDescriptor FinalModel { get; } = new(
        "u2net.onnx",
        "https://github.com/danielgatis/rembg/releases/download/v0.0.0/u2net.onnx",
        "8d10d2f3bb75ae3b6d527c77944fc5e7dcd94b29809d47a739a7a728a912b491",
        176_313_193,
        Required: false);

    public static IReadOnlyList<AiModelDescriptor> All { get; } = [PreviewModel, FinalModel];

    /// <summary>Matches a configured settings path back to a catalog entry by file name, so an admin
    /// who repointed <see cref="Core.Models.AiSettings.PreviewModelPath"/> at some other model gets
    /// "unknown model, fetch it yourself" rather than this silently downloading U-2-Net over it.</summary>
    public static AiModelDescriptor? FindByConfiguredPath(string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return null;
        }

        // Split on both separators rather than Path.GetFileName: an admin on Windows types
        // backslashes, but this also runs on Linux (tests, devcontainer) where '\' is a legal
        // filename character and GetFileName would hand back the whole path.
        var fileName = configuredPath[(configuredPath.LastIndexOfAny(['/', '\\']) + 1)..];
        return All.FirstOrDefault(m => string.Equals(m.FileName, fileName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Resolves a settings path (e.g. "data/models/u2netp.onnx") against the app's own
    /// directory rather than the process working directory, which is not reliably the exe's folder.
    /// Shared by the provider that loads models and the service that downloads them, so the two can
    /// never disagree about where a model lives.</summary>
    public static string ResolvePath(string configuredPath) =>
        Path.IsPathRooted(configuredPath) ? configuredPath : Path.Combine(AppContext.BaseDirectory, configuredPath);
}
