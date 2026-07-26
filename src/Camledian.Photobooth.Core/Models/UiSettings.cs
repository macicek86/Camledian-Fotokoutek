namespace Camledian.Photobooth.Core.Models;

public class UiSettings
{
    public bool KioskMode { get; set; } = true;
    public int CountdownSeconds { get; set; } = 3;
    public int ResultScreenTimeoutSeconds { get; set; } = 30;
    public int ProcessingTimeoutSeconds { get; set; } = 20;
    public string AdminPin { get; set; } = "1234";
    public string ActiveOutputTemplateId { get; set; } = "digital-landscape";
    public BackgroundRemovalMode BackgroundRemovalMode { get; set; } = BackgroundRemovalMode.GreenScreen;

    /// <summary>Physical shutter trigger (spec §57): the name of a System.Windows.Input.Key value
    /// (e.g. "Space", "Enter", "MediaPlayPause"). Most photobooth remotes — Bluetooth shutter
    /// buttons, USB footswitches, presentation clickers — emulate a keyboard keypress, so listening
    /// for a configurable key covers the overwhelming majority of hardware without needing
    /// device-specific drivers. Null/empty disables the physical trigger.</summary>
    public string? PhotoTriggerKey { get; set; } = "Space";
}
