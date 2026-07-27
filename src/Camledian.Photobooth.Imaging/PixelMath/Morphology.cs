namespace Camledian.Photobooth.Imaging.PixelMath;

/// <summary>Separable morphological operations over a single-channel float mask. Used to close the
/// pinholes background subtraction punches into a subject whose clothing happens to match the
/// background colour — dilate then erode fills small holes while leaving the outline where it was.
/// </summary>
public static class Morphology
{
    /// <summary>Dilate then erode by the same radius: fills holes and hairline cracks up to about
    /// 2*radius across. Never removes foreground that survived the dilation, so thin structures —
    /// fingers, a prop's handle, a sparkler — are safe.</summary>
    public static void Close(float[] buffer, int width, int height, double radiusPixels)
    {
        var radius = (int)Math.Round(radiusPixels);
        if (radius <= 0)
        {
            return;
        }

        var temp = new float[buffer.Length];
        Pass(buffer, temp, width, height, radius, dilate: true);
        Pass(temp, buffer, width, height, radius, dilate: false);
    }

    /// <summary>One full dilation or erosion, separated into a horizontal and a vertical sweep. That
    /// is exact for min/max over a rectangular window, which is what we want here — a square window's
    /// slightly boxy corners are invisible after the feather blur that follows.</summary>
    private static void Pass(float[] src, float[] dst, int width, int height, int radius, bool dilate)
    {
        var intermediate = new float[src.Length];

        for (var y = 0; y < height; y++)
        {
            var rowStart = y * width;
            for (var x = 0; x < width; x++)
            {
                var value = src[rowStart + x];
                var from = Math.Max(0, x - radius);
                var to = Math.Min(width - 1, x + radius);
                for (var k = from; k <= to; k++)
                {
                    var candidate = src[rowStart + k];
                    value = dilate ? Math.Max(value, candidate) : Math.Min(value, candidate);
                }

                intermediate[rowStart + x] = value;
            }
        }

        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
            {
                var value = intermediate[(y * width) + x];
                var from = Math.Max(0, y - radius);
                var to = Math.Min(height - 1, y + radius);
                for (var k = from; k <= to; k++)
                {
                    var candidate = intermediate[(k * width) + x];
                    value = dilate ? Math.Max(value, candidate) : Math.Min(value, candidate);
                }

                dst[(y * width) + x] = value;
            }
        }
    }
}
