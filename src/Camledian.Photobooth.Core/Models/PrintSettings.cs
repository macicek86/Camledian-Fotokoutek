namespace Camledian.Photobooth.Core.Models;

public enum PrintOrientation
{
    Portrait,
    Landscape,
}

public class PrintSettings
{
    public string? PrinterName { get; set; }
    public string PaperSize { get; set; } = "10x15";
    public PrintOrientation Orientation { get; set; } = PrintOrientation.Portrait;
    public int Copies { get; set; } = 1;
    public bool AutoPrint { get; set; } = false;
}
