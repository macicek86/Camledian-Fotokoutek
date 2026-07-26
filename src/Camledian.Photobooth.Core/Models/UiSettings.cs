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
}
