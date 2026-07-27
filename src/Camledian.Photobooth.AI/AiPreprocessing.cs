using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Camledian.Photobooth.AI;

/// <summary>Image &lt;-&gt; tensor conversion for the U2Net-family segmentation model (spec §23):
/// resize, ImageNet-style normalization in, min-max-normalized saliency mask out, then a bilinear
/// resize of that mask back up to the original frame resolution.</summary>
internal static class AiPreprocessing
{
    private static readonly float[] Mean = [0.485f, 0.456f, 0.406f];
    private static readonly float[] StdDev = [0.229f, 0.224f, 0.225f];

    public static DenseTensor<float> ToNormalizedTensor(Image<Rgba32> resizedImage)
    {
        var width = resizedImage.Width;
        var height = resizedImage.Height;
        var tensor = new DenseTensor<float>([1, 3, height, width]);

        resizedImage.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < width; x++)
                {
                    var px = row[x];
                    tensor[0, 0, y, x] = (((px.R / 255f) - Mean[0]) / StdDev[0]);
                    tensor[0, 1, y, x] = (((px.G / 255f) - Mean[1]) / StdDev[1]);
                    tensor[0, 2, y, x] = (((px.B / 255f) - Mean[2]) / StdDev[2]);
                }
            }
        });

        return tensor;
    }

    /// <summary>Below this raw min-max spread the model has not actually separated anything from
    /// anything — a uniformly ~0.5 saliency map on a noisy, badly lit frame. Stretching that to 0-1
    /// manufactures a confident-looking mask out of sensor noise, which is worse than admitting the
    /// inference failed, so callers get told instead (see the <c>lowConfidence</c> output).</summary>
    private const float MinMeaningfulRange = 0.10f;

    /// <summary>Reads the last two tensor dimensions as (height, width) and applies rembg-style
    /// min-max contrast normalization so the saliency map spans the full 0-1 range.</summary>
    public static float[] ExtractAndNormalizeMask(Tensor<float> outputTensor, int width, int height) =>
        ExtractAndNormalizeMask(outputTensor, width, height, out _);

    /// <param name="lowConfidence">True when the raw output had almost no contrast, i.e. the model
    /// found nothing it could tell apart. The mask is still normalized and returned (it is the best
    /// guess available), but callers should prefer not to key a photo on it.</param>
    /// <inheritdoc cref="ExtractAndNormalizeMask(Tensor{float}, int, int)"/>
    public static float[] ExtractAndNormalizeMask(Tensor<float> outputTensor, int width, int height, out bool lowConfidence)
    {
        var dims = outputTensor.Dimensions;
        var h = dims[^2];
        var w = dims[^1];

        var raw = new float[h * w];
        var min = float.MaxValue;
        var max = float.MinValue;

        var flatIndex = 0;
        // Tensor could be [1,1,H,W] or [1,H,W]; iterate the trailing H*W elements in order regardless.
        var totalTrailing = h * w;
        var offset = (int)outputTensor.Length - totalTrailing;
        for (var i = 0; i < totalTrailing; i++)
        {
            var value = GetFlat(outputTensor, offset + i);
            raw[flatIndex++] = value;
            if (value < min)
            {
                min = value;
            }

            if (value > max)
            {
                max = value;
            }
        }

        var rawRange = max - min;
        lowConfidence = rawRange < MinMeaningfulRange;

        var range = Math.Max(1e-6f, rawRange);
        for (var i = 0; i < raw.Length; i++)
        {
            raw[i] = (raw[i] - min) / range;
        }

        return h == height && w == width ? raw : ResizeMask(raw, w, h, width, height);
    }

    private static float GetFlat(Tensor<float> tensor, int flatIndex)
    {
        // DenseTensor<T> exposes contiguous backing storage; ToArray() (called once by the caller's
        // loop start) would be simpler but Tensor<T> doesn't guarantee a fast path, so index directly
        // via the tensor's own flat accessor.
        return tensor.GetValue(flatIndex);
    }

    /// <summary>Simple bilinear resize of a single-channel mask, used both to upscale the model's
    /// fixed-size output to the source frame resolution and, defensively, if a model's declared
    /// output size doesn't match the configured input size.</summary>
    public static float[] ResizeMask(float[] mask, int srcWidth, int srcHeight, int destWidth, int destHeight)
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
