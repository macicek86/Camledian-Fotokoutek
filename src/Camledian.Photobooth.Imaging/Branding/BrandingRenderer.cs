using Camledian.Photobooth.Core.Models;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Camledian.Photobooth.Imaging.Branding;

/// <summary>
/// Stamps admin-configured branding (corner logo + text banner) onto an already-composed image.
/// Runs once per capture on the final render — deliberately not per preview frame, where its cost
/// (font layout, logo decode/resize) would fight the camera frame rate for no benefit.
/// </summary>
public static class BrandingRenderer
{
    // Windows kiosk first, then fonts that actually exist on the Linux/macOS dev machines running
    // the test suite; SystemFonts.Families.First() is the final "anything installed" resort.
    private static readonly string[] PreferredFontFamilies =
        ["Segoe UI", "Arial", "Helvetica", "DejaVu Sans", "Liberation Sans", "Noto Sans"];

    /// <summary>Applies branding in place. Missing logo file or no usable system font just skips
    /// that element (never fails the capture — same philosophy as the printing/AI fallbacks).</summary>
    public static void Apply(Image<Rgba32> image, BrandingSettings settings)
    {
        if (!settings.Enabled)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(settings.BannerText))
        {
            DrawBanner(image, settings);
        }

        if (!string.IsNullOrWhiteSpace(settings.LogoPath))
        {
            DrawLogo(image, settings);
        }
    }

    private static void DrawLogo(Image<Rgba32> image, BrandingSettings settings)
    {
        var logoPath = ResolvePath(settings.LogoPath!);
        if (!File.Exists(logoPath))
        {
            return;
        }

        using var logo = Image.Load<Rgba32>(logoPath);

        var targetWidth = Math.Max(8, (int)(image.Width * Math.Clamp(settings.LogoWidthPercent, 1, 60) / 100.0));
        var targetHeight = Math.Max(8, (int)((double)logo.Height / logo.Width * targetWidth));
        logo.Mutate(ctx => ctx.Resize(targetWidth, targetHeight));

        var margin = (int)(image.Width * Math.Clamp(settings.LogoMarginPercent, 0, 20) / 100.0);
        var x = settings.LogoCorner is LogoCorner.TopLeft or LogoCorner.BottomLeft
            ? margin
            : image.Width - targetWidth - margin;
        var y = settings.LogoCorner is LogoCorner.TopLeft or LogoCorner.TopRight
            ? margin
            : image.Height - targetHeight - margin;

        image.Mutate(ctx => ctx.DrawImage(logo, new Point(x, y), 1f));
    }

    private static void DrawBanner(Image<Rgba32> image, BrandingSettings settings)
    {
        if (!TryResolveFont(out var fontFamily))
        {
            return;
        }

        var fontSize = (float)(image.Height * Math.Clamp(settings.FontSizePercent, 1, 20) / 100.0);
        var font = fontFamily.CreateFont(fontSize, FontStyle.Bold);
        var textColor = ParseHexOr(settings.TextColorHex, Color.White);

        var text = settings.BannerText!;
        var measured = TextMeasurer.MeasureBounds(text, new TextOptions(font));
        var barHeight = fontSize * 1.8f;
        var barY = settings.BannerPosition == BannerPosition.Top ? 0 : image.Height - barHeight;

        image.Mutate(ctx =>
        {
            if (!string.IsNullOrWhiteSpace(settings.BannerBackgroundHex) &&
                TryParseHex(settings.BannerBackgroundHex, out var barColor))
            {
                ctx.Fill(barColor, new RectangleF(0, barY, image.Width, barHeight));
            }

            var textX = (image.Width - measured.Width) / 2f;
            var textY = barY + ((barHeight - measured.Height) / 2f) - measured.Top;
            ctx.DrawText(text, font, textColor, new PointF(textX, textY));
        });
    }

    private static bool TryResolveFont(out FontFamily fontFamily)
    {
        foreach (var name in PreferredFontFamilies)
        {
            if (SystemFonts.TryGet(name, out fontFamily))
            {
                return true;
            }
        }

        // Any installed font beats silently dropping the banner on an unusual system.
        var first = SystemFonts.Families.FirstOrDefault();
        fontFamily = first;
        return first.Name is not null;
    }

    private static Color ParseHexOr(string hex, Color fallback) => TryParseHex(hex, out var color) ? color : fallback;

    private static bool TryParseHex(string hex, out Color color) => Color.TryParseHex(hex.TrimStart('#'), out color);

    private static string ResolvePath(string configuredPath) =>
        Path.IsPathRooted(configuredPath) ? configuredPath : Path.Combine(AppContext.BaseDirectory, configuredPath);
}
