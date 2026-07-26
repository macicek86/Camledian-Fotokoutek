namespace Camledian.Photobooth.Imaging.ChromaKey;

internal static class ColorMath
{
    /// <summary>Converts 8-bit RGB to HSV with hue in degrees [0,360), saturation/value in [0,1].</summary>
    public static (double H, double S, double V) RgbToHsv(byte r, byte g, byte b)
    {
        double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0;
        var max = Math.Max(rf, Math.Max(gf, bf));
        var min = Math.Min(rf, Math.Min(gf, bf));
        var delta = max - min;

        double h;
        if (delta < 1e-9)
        {
            h = 0;
        }
        else if (max == rf)
        {
            h = 60 * (((gf - bf) / delta) % 6);
        }
        else if (max == gf)
        {
            h = 60 * (((bf - rf) / delta) + 2);
        }
        else
        {
            h = 60 * (((rf - gf) / delta) + 4);
        }

        if (h < 0)
        {
            h += 360;
        }

        var s = max < 1e-9 ? 0 : delta / max;
        var v = max;
        return (h, s, v);
    }

    /// <summary>Shortest angular distance between two hues in degrees, always in [0,180].</summary>
    public static double HueDistance(double a, double b)
    {
        var d = Math.Abs(a - b) % 360;
        return d > 180 ? 360 - d : d;
    }

    public static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

    /// <summary>Hermite smoothstep, used to compress muddy mid-tone alpha into a cleaner edge.</summary>
    public static double SmoothStep(double edge0, double edge1, double x)
    {
        if (Math.Abs(edge1 - edge0) < 1e-9)
        {
            return x < edge0 ? 0 : 1;
        }

        var t = Clamp01((x - edge0) / (edge1 - edge0));
        return t * t * (3 - 2 * t);
    }
}
