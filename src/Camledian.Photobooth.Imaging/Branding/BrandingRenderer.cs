using Path = System.IO.Path;
using Camledian.Photobooth.Core.Models;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Camledian.Photobooth.Imaging.Branding;

/// <summary>
/// Stamps admin-configured branding (logo + text banner in one of several pre-made styles) onto an
/// already-composed image. Runs once per capture on the final render — deliberately not per preview
/// frame, where its cost (font layout, logo decode/resize) would fight the camera frame rate for no
/// benefit.
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

        int x, y;
        if (settings.LogoCorner == LogoCorner.Custom)
        {
            x = (int)(image.Width * Math.Clamp(settings.LogoXPercent, 0, 100) / 100.0);
            y = (int)(image.Height * Math.Clamp(settings.LogoYPercent, 0, 100) / 100.0);
            // Keep the logo fully on-canvas even at 100%/100%.
            x = Math.Min(x, image.Width - targetWidth);
            y = Math.Min(y, image.Height - targetHeight);
        }
        else
        {
            var margin = (int)(image.Width * Math.Clamp(settings.LogoMarginPercent, 0, 20) / 100.0);
            x = settings.LogoCorner is LogoCorner.TopLeft or LogoCorner.BottomLeft
                ? margin
                : image.Width - targetWidth - margin;
            y = settings.LogoCorner is LogoCorner.TopLeft or LogoCorner.TopRight
                ? margin
                : image.Height - targetHeight - margin;
        }

        image.Mutate(ctx => ctx.DrawImage(logo, new Point(Math.Max(0, x), Math.Max(0, y)), 1f));
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
        var bandHeight = fontSize * 1.8f;

        // Vertical: center of the band at Top margin / Bottom margin / free percent.
        var bandCenterY = settings.BannerPosition switch
        {
            BannerPosition.Top => bandHeight / 2f,
            BannerPosition.Bottom => image.Height - (bandHeight / 2f),
            _ => (float)(image.Height * Math.Clamp(settings.BannerYPercent, 0, 100) / 100.0),
        };
        var bandY = Math.Clamp(bandCenterY - (bandHeight / 2f), 0, Math.Max(0, image.Height - bandHeight));

        // Horizontal: where the text itself sits.
        var horizontalMargin = (float)(image.Width * Math.Clamp(settings.BannerMarginPercent, 0, 40) / 100.0);
        var textX = settings.BannerAlignment switch
        {
            BannerAlignment.Left => horizontalMargin,
            BannerAlignment.Right => image.Width - measured.Width - horizontalMargin,
            _ => (image.Width - measured.Width) / 2f,
        };
        var textY = bandY + ((bandHeight - measured.Height) / 2f) - measured.Top;

        Color fillColor = default;
        var hasFill = !string.IsNullOrWhiteSpace(settings.BannerBackgroundHex) &&
                      TryParseHex(settings.BannerBackgroundHex!, out fillColor);

        var accentColor = ParseHexOr(settings.AccentColorHex, Color.ParseHex("D4AF37"));

        image.Mutate(ctx =>
        {
            switch (settings.BannerStyle)
            {
                case BannerStyle.Pill:
                    if (hasFill)
                    {
                        // Capsule = center rectangle + half-circle caps, padded around the text.
                        // Drawn opaque into a scratch buffer first, with the fill's alpha applied
                        // only at composite time — filling the overlapping shapes directly with a
                        // semi-transparent color would double-blend the overlap into darker crescents.
                        var padX = fontSize * 0.9f;
                        var pillHeight = bandHeight;
                        var pillWidth = measured.Width + (2 * padX);
                        var pillX = textX - padX;
                        var radius = pillHeight / 2f;

                        var fillRgba = fillColor.ToPixel<Rgba32>();
                        var opaqueFill = new Color(new Rgba32(fillRgba.R, fillRgba.G, fillRgba.B));
                        var scratchWidth = Math.Max(1, (int)MathF.Ceiling(pillWidth));
                        var scratchHeight = Math.Max(1, (int)MathF.Ceiling(pillHeight));
                        using var pillScratch = new Image<Rgba32>(scratchWidth, scratchHeight);
                        pillScratch.Mutate(scratch =>
                        {
                            scratch.Fill(opaqueFill, new RectangleF(radius, 0, pillWidth - (2 * radius), pillHeight));
                            scratch.Fill(opaqueFill, new EllipsePolygon(radius, radius, radius));
                            scratch.Fill(opaqueFill, new EllipsePolygon(pillWidth - radius, radius, radius));
                        });

                        var pillPoint = new Point((int)MathF.Round(Math.Max(0, pillX)), (int)MathF.Round(bandY));
                        ctx.DrawImage(pillScratch, pillPoint, fillRgba.A / 255f);
                    }

                    break;

                case BannerStyle.Ribbon:
                    if (hasFill)
                    {
                        ctx.Fill(fillColor, new RectangleF(0, bandY, image.Width, bandHeight));
                    }

                    var lineThickness = Math.Max(2f, fontSize * 0.08f);
                    ctx.Fill(accentColor, new RectangleF(0, bandY, image.Width, lineThickness));
                    ctx.Fill(accentColor, new RectangleF(0, bandY + bandHeight - lineThickness, image.Width, lineThickness));
                    break;

                case BannerStyle.Minimal:
                    // No background; the drop shadow below is what keeps the text readable.
                    break;

                default: // Bar
                    if (hasFill)
                    {
                        ctx.Fill(fillColor, new RectangleF(0, bandY, image.Width, bandHeight));
                    }

                    break;
            }

            if (settings.BannerStyle == BannerStyle.Minimal)
            {
                var shadowOffset = Math.Max(1f, fontSize * 0.06f);
                ctx.DrawText(text, font, Color.FromRgba(0, 0, 0, 160), new PointF(textX + shadowOffset, textY + shadowOffset));
            }

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
