namespace Camledian.Photobooth.Core.Models;

/// <summary>
/// HSV-space chroma key parameters, editable live by the admin. All angles/percentages are stored
/// so the admin screen can bind directly to sliders without extra conversion.
/// </summary>
public class ChromaKeySettings
{
    /// <summary>Target key color in degrees (0-360) on the HSV hue wheel. 120 = pure green, 240 = pure blue.</summary>
    public double TargetHueDegrees { get; set; } = 120;

    /// <summary>How many degrees either side of <see cref="TargetHueDegrees"/> count as background.</summary>
    public double HueToleranceDegrees { get; set; } = 25;

    /// <summary>Minimum saturation (0-1) for a pixel to be considered part of the key color.</summary>
    public double SaturationThreshold { get; set; } = 0.20;

    /// <summary>Minimum value/brightness (0-1) for a pixel to be considered part of the key color.</summary>
    public double ValueThreshold { get; set; } = 0.20;

    /// <summary>Width in degrees/fraction over which the mask fades rather than hard-cuts, in pixels
    /// of blur radius applied to the alpha mask. Removes jagged/aliased edges.</summary>
    public double FeatherPixels { get; set; } = 3.0;

    /// <summary>Extra radius (in pixels) of edge erosion+feather applied around the foreground/background
    /// boundary to smooth stair-stepping from the hard threshold.</summary>
    public double EdgeSmoothingPixels { get; set; } = 2.0;

    /// <summary>How strongly green/blue spill on the subject's edges (hair, skin) is desaturated
    /// toward neutral gray, 0 = off, 1 = full suppression.</summary>
    public double SpillSuppressionStrength { get; set; } = 0.6;

    public ChromaKeySettings Clone() => (ChromaKeySettings)MemberwiseClone();
}
