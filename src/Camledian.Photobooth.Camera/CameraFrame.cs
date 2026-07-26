using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Camledian.Photobooth.Camera;

public sealed class CameraFrame : IDisposable
{
    public required Image<Rgba32> Image { get; init; }
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    public long FrameNumber { get; init; }

    public void Dispose() => Image.Dispose();
}
