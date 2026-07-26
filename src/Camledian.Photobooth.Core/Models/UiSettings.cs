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

    /// <summary>How many shots to take in one go (burst). 1 = single shot, straight to processing;
    /// 2+ shows a selection screen where the guest picks their favorite before processing.</summary>
    public int BurstCount { get; set; } = 3;

    /// <summary>Pause between burst shots, in milliseconds — enough time to change the pose.</summary>
    public int BurstIntervalMs { get; set; } = 1500;

    /// <summary>Prompt flashed over the live preview while shots are being taken.</summary>
    public string SmilePromptText { get; set; } = "Úsměv! 😊";

    /// <summary>Physical shutter trigger (spec §57): the name of a System.Windows.Input.Key value
    /// (e.g. "Space", "Enter", "MediaPlayPause"). Most photobooth remotes — Bluetooth shutter
    /// buttons, USB footswitches, presentation clickers — emulate a keyboard keypress, so listening
    /// for a configurable key covers the overwhelming majority of hardware without needing
    /// device-specific drivers. Null/empty disables the physical trigger.</summary>
    public string? PhotoTriggerKey { get; set; } = "Space";
}
