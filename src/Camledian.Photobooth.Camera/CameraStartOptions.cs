namespace Camledian.Photobooth.Camera;

/// <param name="LockExposureAndWhiteBalance">Switch the camera to manual exposure and white balance
/// once it has metered the scene. A webcam left on auto re-meters the moment a guest steps in front
/// of it, which shifts every pixel at once and is the single biggest source of trouble for
/// reference-photo background subtraction.</param>
public sealed record CameraStartOptions(
    string? DeviceId,
    int Width,
    int Height,
    int Fps,
    bool LockExposureAndWhiteBalance = true)
{
    public static CameraStartOptions Default { get; } = new(null, 1280, 720, 30);
}
