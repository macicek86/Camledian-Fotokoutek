using System.Drawing.Printing;
using Camledian.Photobooth.Core.Models;
using Microsoft.Extensions.Logging;

namespace Camledian.Photobooth.Printing;

/// <summary>Real Windows printing via System.Drawing.Printing. Requires a Windows machine (with or
/// without an actual physical printer — "no printer installed" is handled as a normal PrintResult
/// failure, not an exception, per spec §28/§46.</summary>
public class WindowsPrintingService(ILogger<WindowsPrintingService> logger) : IPrintingService
{
    public IReadOnlyList<PrinterInfo> ListPrinters()
    {
        var defaultName = new PrinterSettings().PrinterName;
        var printers = new List<PrinterInfo>();
        foreach (string name in PrinterSettings.InstalledPrinters)
        {
            printers.Add(new PrinterInfo(name, string.Equals(name, defaultName, StringComparison.Ordinal)));
        }

        return printers;
    }

    public Task<PrintResult> PrintAsync(string imagePath, PrintSettings settings, CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                try
                {
                    using var image = System.Drawing.Image.FromFile(imagePath);
                    using var document = new PrintDocument();
                    if (!string.IsNullOrWhiteSpace(settings.PrinterName))
                    {
                        document.PrinterSettings.PrinterName = settings.PrinterName;
                    }

                    if (!document.PrinterSettings.IsValid)
                    {
                        var message = $"Printer '{settings.PrinterName}' is not installed or not valid.";
                        logger.LogWarning("Print failed: {Message}", message);
                        return new PrintResult(false, message);
                    }

                    document.PrinterSettings.Copies = (short)Math.Clamp(settings.Copies, 1, 99);
                    document.DefaultPageSettings.Landscape = settings.Orientation == PrintOrientation.Landscape;

                    document.PrintPage += (_, e) =>
                    {
                        if (e.Graphics is null || e.MarginBounds.Width <= 0 || e.MarginBounds.Height <= 0)
                        {
                            return;
                        }

                        var bounds = e.MarginBounds;
                        var ratio = Math.Min(
                            (double)bounds.Width / image.Width,
                            (double)bounds.Height / image.Height);
                        var width = (int)(image.Width * ratio);
                        var height = (int)(image.Height * ratio);
                        var x = bounds.X + ((bounds.Width - width) / 2);
                        var y = bounds.Y + ((bounds.Height - height) / 2);
                        e.Graphics.DrawImage(image, x, y, width, height);
                    };

                    document.Print();
                    logger.LogInformation("Printed {ImagePath} to {Printer} ({Copies} copies).", imagePath, document.PrinterSettings.PrinterName, settings.Copies);
                    return new PrintResult(true, null);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Print failed for {ImagePath}.", imagePath);
                    return new PrintResult(false, ex.Message);
                }
            },
            cancellationToken);
}
