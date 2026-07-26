namespace Camledian.Photobooth.Core.Models;

public enum LogoCorner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,

    /// <summary>Free placement via <see cref="BrandingSettings.LogoXPercent"/> /
    /// <see cref="BrandingSettings.LogoYPercent"/> instead of snapping to a corner.</summary>
    Custom,
}

public enum BannerPosition
{
    Top,
    Bottom,

    /// <summary>Free vertical placement via <see cref="BrandingSettings.BannerYPercent"/>.</summary>
    Custom,
}

public enum BannerAlignment
{
    Left,
    Center,
    Right,
}

/// <summary>Pre-made visual styles for the text banner.</summary>
public enum BannerStyle
{
    /// <summary>Full-width solid bar behind the text (the original style).</summary>
    Bar,

    /// <summary>Rounded capsule sized to the text, positioned by alignment — good over busy photos.</summary>
    Pill,

    /// <summary>Full-width bar with thin accent lines above and below — the "elegant/wedding" look.</summary>
    Ribbon,

    /// <summary>No background at all, just the text with a soft drop shadow for legibility.</summary>
    Minimal,
}

/// <summary>
/// Lightweight branding stamped onto the final photo (logo + text banner), configured entirely in
/// Admin — no need to produce a full-canvas overlay PNG for simple "event name + logo" cases.
/// Full-canvas overlays (assets/overlays) still exist for richer frame designs; branding is applied
/// on top of everything, after composition.
/// </summary>
public class BrandingSettings
{
    public bool Enabled { get; set; }

    /// <summary>Path to a PNG logo (alpha respected — use a transparent PNG for clean edges).
    /// Relative paths resolve against the app base directory. Null/empty = no logo.</summary>
    public string? LogoPath { get; set; }

    public LogoCorner LogoCorner { get; set; } = LogoCorner.BottomRight;

    /// <summary>Logo width as a percentage of the output image width.</summary>
    public double LogoWidthPercent { get; set; } = 15;

    /// <summary>Margin from the image edges, as a percentage of the output image width. Ignored for
    /// <see cref="LogoCorner.Custom"/>.</summary>
    public double LogoMarginPercent { get; set; } = 2;

    /// <summary>Logo left edge as a percentage of image width, used with <see cref="LogoCorner.Custom"/>.</summary>
    public double LogoXPercent { get; set; } = 80;

    /// <summary>Logo top edge as a percentage of image height, used with <see cref="LogoCorner.Custom"/>.</summary>
    public double LogoYPercent { get; set; } = 80;

    /// <summary>Banner text, e.g. "Svatba Jana & Petr — 26. 7. 2026". Null/empty = no banner.</summary>
    public string? BannerText { get; set; }

    public BannerStyle BannerStyle { get; set; } = BannerStyle.Bar;

    public BannerPosition BannerPosition { get; set; } = BannerPosition.Bottom;

    /// <summary>Vertical center of the banner as a percentage of image height, used with
    /// <see cref="BannerPosition.Custom"/>.</summary>
    public double BannerYPercent { get; set; } = 90;

    /// <summary>Horizontal alignment of the text (and of the pill in the Pill style).</summary>
    public BannerAlignment BannerAlignment { get; set; } = BannerAlignment.Center;

    /// <summary>Horizontal margin from the image edge for Left/Right alignment, as a percentage of
    /// image width.</summary>
    public double BannerMarginPercent { get; set; } = 4;

    /// <summary>Font size as a percentage of the output image height.</summary>
    public double FontSizePercent { get; set; } = 4;

    /// <summary>Text color as #RRGGBB or #RRGGBBAA.</summary>
    public string TextColorHex { get; set; } = "#FFFFFF";

    /// <summary>Bar/pill fill behind the text (#RRGGBB or #RRGGBBAA, alpha supported); null/empty =
    /// no fill even in Bar/Pill/Ribbon styles.</summary>
    public string? BannerBackgroundHex { get; set; } = "#0D1B2E99";

    /// <summary>Accent color for the Ribbon style's thin lines. Defaults to the brand gold.</summary>
    public string AccentColorHex { get; set; } = "#D4AF37";
}
