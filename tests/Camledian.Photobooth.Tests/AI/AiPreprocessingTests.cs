using Camledian.Photobooth.AI;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Camledian.Photobooth.Tests.AI;

/// <summary>
/// Covers the min-max normalization the U-2-Net masks go through. The interesting case is the
/// degenerate one: a frame the model made nothing of. Stretching that to 0-1 turns sensor noise into
/// a confident-looking cutout, which is how a badly lit shot ends up with a random blob of a guest
/// keyed out — so the low-confidence flag has to fire.
/// </summary>
public class AiPreprocessingTests
{
    private static DenseTensor<float> Mask(int size, Func<int, int, float> value)
    {
        var tensor = new DenseTensor<float>([1, 1, size, size]);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                tensor[0, 0, y, x] = value(x, y);
            }
        }

        return tensor;
    }

    [Fact]
    public void ConfidentMaskIsStretchedToFullRange()
    {
        // A clear subject: half the frame at 0.9, half at 0.1.
        var tensor = Mask(8, (x, _) => x < 4 ? 0.9f : 0.1f);

        var mask = AiPreprocessing.ExtractAndNormalizeMask(tensor, 8, 8, out var lowConfidence);

        Assert.False(lowConfidence);
        Assert.Equal(1f, mask[0], 3);
        Assert.Equal(0f, mask[7], 3);
    }

    [Fact]
    public void FlatMaskIsReportedAsLowConfidence()
    {
        // The model gave everything the same middling score — it separated nothing.
        var tensor = Mask(8, (x, _) => 0.5f + (x * 0.001f));

        AiPreprocessing.ExtractAndNormalizeMask(tensor, 8, 8, out var lowConfidence);

        Assert.True(lowConfidence);
    }

    [Fact]
    public void MaskAtTheConfidenceBoundaryIsNotFlagged()
    {
        var tensor = Mask(8, (x, _) => x < 4 ? 0.5f : 0.35f); // range 0.15, comfortably meaningful

        AiPreprocessing.ExtractAndNormalizeMask(tensor, 8, 8, out var lowConfidence);

        Assert.False(lowConfidence);
    }

    [Fact]
    public void MaskIsResizedToTheRequestedResolution()
    {
        var tensor = Mask(4, (x, _) => x < 2 ? 1f : 0f);

        var mask = AiPreprocessing.ExtractAndNormalizeMask(tensor, 16, 16, out _);

        Assert.Equal(16 * 16, mask.Length);
        Assert.True(mask[0] > 0.9f);
        Assert.True(mask[15] < 0.1f);
    }
}
