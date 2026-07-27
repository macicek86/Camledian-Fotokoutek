namespace Camledian.Photobooth.Imaging.PixelMath;

/// <summary>Bilinear resampling of a single-channel float mask. Needed wherever a mask is computed at
/// one resolution and applied at another — the AI models produce a fixed-size saliency map, and
/// background subtraction can compare at half resolution to halve the sensor noise.</summary>
public static class MaskResize
{
    public static float[] Bilinear(float[] mask, int srcWidth, int srcHeight, int destWidth, int destHeight)
    {
        if (srcWidth == destWidth && srcHeight == destHeight)
        {
            return mask;
        }

        var result = new float[destWidth * destHeight];
        var xRatio = srcWidth / (float)destWidth;
        var yRatio = srcHeight / (float)destHeight;

        for (var y = 0; y < destHeight; y++)
        {
            var srcY = Math.Min(srcHeight - 1.001f, y * yRatio);
            var y0 = (int)srcY;
            var y1 = Math.Min(srcHeight - 1, y0 + 1);
            var yFrac = srcY - y0;

            for (var x = 0; x < destWidth; x++)
            {
                var srcX = Math.Min(srcWidth - 1.001f, x * xRatio);
                var x0 = (int)srcX;
                var x1 = Math.Min(srcWidth - 1, x0 + 1);
                var xFrac = srcX - x0;

                var top = (mask[(y0 * srcWidth) + x0] * (1 - xFrac)) + (mask[(y0 * srcWidth) + x1] * xFrac);
                var bottom = (mask[(y1 * srcWidth) + x0] * (1 - xFrac)) + (mask[(y1 * srcWidth) + x1] * xFrac);
                result[(y * destWidth) + x] = (top * (1 - yFrac)) + (bottom * yFrac);
            }
        }

        return result;
    }
}
