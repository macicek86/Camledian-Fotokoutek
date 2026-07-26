namespace Camledian.Photobooth.Core.Models;

public class AiSettings
{
    /// <summary>Small/fast model used for the live preview loop (spec §24) — needs to keep up with
    /// the camera, not be maximally accurate. Relative to the app's base directory. Downloaded by
    /// scripts/download-models.ps1, never committed to git.</summary>
    public string PreviewModelPath { get; set; } = "models/u2netp.onnx";

    /// <summary>Larger, more accurate model run once after capture (spec §25) — final quality
    /// matters more than speed here, since it's a single inference rather than a per-frame one.
    /// Falls back to <see cref="PreviewModelPath"/> if this file isn't present.</summary>
    public string FinalModelPath { get; set; } = "models/u2net.onnx";

    public bool PreferDirectML { get; set; } = true;

    /// <summary>Square input size both models expect, e.g. 320 for the U2Net family (same for the
    /// "p" preview variant and the full final-quality one).</summary>
    public int InputSize { get; set; } = 320;

    /// <summary>Target inference rate for the live preview; the last mask is reused between runs
    /// (camera can run at 30 FPS while AI runs at ~10-15 FPS).</summary>
    public int PreviewInferenceFps { get; set; } = 12;

    /// <summary>Mask alpha threshold (0-1) below which a pixel is treated as fully background in the
    /// fast preview path.</summary>
    public double PreviewMaskThreshold { get; set; } = 0.5;

    public double FeatherPixels { get; set; } = 4.0;
}
