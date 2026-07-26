namespace Camledian.Photobooth.Imaging.PixelMath;

/// <summary>Separable box blur over a single-channel float buffer. Used to feather a hard mask
/// threshold (chroma key or AI saliency) into a soft alpha ramp instead of a jagged binary edge.</summary>
public static class BoxBlur
{
    public static void Apply(float[] buffer, int width, int height, double radiusPixels)
    {
        var radius = (int)Math.Round(radiusPixels);
        if (radius <= 0)
        {
            return;
        }

        var temp = new float[buffer.Length];
        HorizontalPass(buffer, temp, width, height, radius);
        VerticalPass(temp, buffer, width, height, radius);
    }

    private static void HorizontalPass(float[] src, float[] dst, int width, int height, int radius)
    {
        for (var y = 0; y < height; y++)
        {
            var rowStart = y * width;
            double sum = 0;
            var count = 0;
            for (var x = -radius; x <= radius; x++)
            {
                if (x < 0 || x >= width)
                {
                    continue;
                }

                sum += src[rowStart + x];
                count++;
            }

            for (var x = 0; x < width; x++)
            {
                dst[rowStart + x] = (float)(sum / count);

                var addX = x + radius + 1;
                var removeX = x - radius;
                if (addX < width)
                {
                    sum += src[rowStart + addX];
                    count++;
                }

                if (removeX >= 0)
                {
                    sum -= src[rowStart + removeX];
                    count--;
                }
            }
        }
    }

    private static void VerticalPass(float[] src, float[] dst, int width, int height, int radius)
    {
        for (var x = 0; x < width; x++)
        {
            double sum = 0;
            var count = 0;
            for (var y = -radius; y <= radius; y++)
            {
                if (y < 0 || y >= height)
                {
                    continue;
                }

                sum += src[(y * width) + x];
                count++;
            }

            for (var y = 0; y < height; y++)
            {
                dst[(y * width) + x] = (float)(sum / count);

                var addY = y + radius + 1;
                var removeY = y - radius;
                if (addY < height)
                {
                    sum += src[(addY * width) + x];
                    count++;
                }

                if (removeY >= 0)
                {
                    sum -= src[(removeY * width) + x];
                    count--;
                }
            }
        }
    }
}
