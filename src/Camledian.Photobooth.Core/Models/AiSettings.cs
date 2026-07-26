namespace Camledian.Photobooth.Core.Models;

public class AiSettings
{
    /// <summary>Relative to the app's models/ directory, e.g. "models/u2netp.onnx". Downloaded by
    /// scripts/download-models.ps1, never committed to git.</summary>
    public string ModelPath { get; set; } = "models/u2netp.onnx";

    public bool PreferDirectML { get; set; } = true;

    /// <summary>Square input size the segmentation model expects, e.g. 320 for U2NetP.</summary>
    public int InputSize { get; set; } = 320;

    /// <summary>Target inference rate for the live preview; the last mask is reused between runs
    /// (camera can run at 30 FPS while AI runs at ~10-15 FPS).</summary>
    public int PreviewInferenceFps { get; set; } = 12;

    /// <summary>Mask alpha threshold (0-1) below which a pixel is treated as fully background in the
    /// fast preview path.</summary>
    public double PreviewMaskThreshold { get; set; } = 0.5;

    public double FeatherPixels { get; set; } = 4.0;
}
