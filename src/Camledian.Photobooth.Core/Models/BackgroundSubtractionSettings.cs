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
}
