namespace Camledian.Photobooth.Camera;

public sealed record CameraStartOptions(string? DeviceId, int Width, int Height, int Fps)
{
    public static CameraStartOptions Default { get; } = new(null, 1280, 720, 30);
}
