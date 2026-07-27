namespace Camledian.Photobooth.Core.Models;

/// <summary>
/// Reference-photo background subtraction: capture one photo of the empty scene, then key out
/// anything in later frames that still matches it closely. No green screen needed — works for any
/// static background, since the camera/booth don't move during an event.
/// </summary>
public class BackgroundSubtractionSettings
{
    /// <summary>Path to the captured empty-scene reference photo, relative to the app base
    /// directory. Null/missing until an admin captures one (spec-inspired UX addition).</summary>
    public string? ReferenceImagePath { get; set; }

    /// <summary>Per-pixel RGB Euclidean distance (0-441) above which a pixel counts as foreground.
    /// Lower = more sensitive (catches subtle shadows as foreground too); higher = more forgiving of
    /// lighting drift since the reference was captured, at the cost of missing faint edges.</summary>
    public double ThresholdDistance { get; set; } = 40;

    public double FeatherPixels { get; set; } = 3.0;

    /// <summary>Rescale the reference photo's brightness/colour to match the current frame before
    /// comparing them. Without it, a webcam that re-meters the scene when someone steps in front of
    /// it (auto-exposure, auto white balance) shifts every pixel at once, the reference stops
    /// matching anywhere, and the whole frame reads as foreground. Costs one extra pass over a
    /// subsample of the frame. Turn off only to reproduce the old behaviour.</summary>
    public bool CompensateLightingDrift { get; set; } = true;

    /// <summary>Radius (px) of the morphological closing that fills pinholes punched into the subject
    /// where their clothing happens to match the background colour behind them. 0 disables it.
    /// Closing can only add foreground, never remove it, so it cannot eat thin props or fingers —
    /// but a large radius will bridge genuinely separate gaps (e.g. between an arm and the body).</summary>
    public double FillHolesPixels { get; set; } = 2.0;
}
