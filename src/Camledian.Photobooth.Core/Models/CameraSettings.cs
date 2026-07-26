namespace Camledian.Photobooth.Core.Models;

public class CameraSettings
{
    /// <summary>Device id as reported by ICameraProvider.ListDevices(); null/empty = auto-pick first.</summary>
    public string? SelectedDeviceId { get; set; }

    public int RequestedWidth { get; set; } = 1280;
    public int RequestedHeight { get; set; } = 720;
    public int RequestedFps { get; set; } = 30;

    /// <summary>When true and no real camera can be opened, the app falls back to MockCameraProvider
    /// instead of failing to start — useful for development and for this Linux devcontainer.</summary>
    public bool UseMockIfUnavailable { get; set; } = true;
}
