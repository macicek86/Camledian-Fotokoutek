namespace Camledian.Photobooth.Core.Models;

public enum LogoCorner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

public enum BannerPosition
{
    Top,
    Bottom,
}

/// <summary>
/// Lightweight branding stamped onto the final photo (logo in a corner + a text banner), configured
/// entirely in Admin — no need to produce a full-canvas overlay PNG for simple "event name + logo"
/// cases. Full-canvas overlays (assets/overlays) still exist for richer frame designs; branding is
/// applied on top of everything, after composition.
/// </summary>
public class BrandingSettings
{
    public bool Enabled { get; set; }

    /// <summary>Path to a PNG logo (alpha respected). Relative paths resolve against the app base
    /// directory. Null/empty = no logo.</summary>
    public string? LogoPath { get; set; }

    public LogoCorner LogoCorner { get; set; } = LogoCorner.BottomRight;

    /// <summary>Logo width as a percentage of the output image width.</summary>
    public double LogoWidthPercent { get; set; } = 15;

    /// <summary>Margin from the image edges, as a percentage of the output image width.</summary>
    public double LogoMarginPercent { get; set; } = 2;

    /// <summary>Banner text, e.g. "Svatba Jana & Petr — 26. 7. 2026". Null/empty = no banner.</summary>
    public string? BannerText { get; set; }

    public BannerPosition BannerPosition { get; set; } = BannerPosition.Bottom;

    /// <summary>Font size as a percentage of the output image height.</summary>
    public double FontSizePercent { get; set; } = 4;

    /// <summary>Text color as #RRGGBB or #RRGGBBAA.</summary>
    public string TextColorHex { get; set; } = "#FFFFFF";

    /// <summary>Optional bar behind the text (#RRGGBB or #RRGGBBAA, alpha supported); null/empty =
    /// text drawn directly over the photo.</summary>
    public string? BannerBackgroundHex { get; set; } = "#0D1B2E99";
}
