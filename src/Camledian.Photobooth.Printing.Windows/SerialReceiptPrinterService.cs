using System.IO.Ports;
using Camledian.Photobooth.Core.Models;
using Microsoft.Extensions.Logging;

namespace Camledian.Photobooth.Printing;

/// <summary>Sends ESC/POS payloads to the configured COM port. A paired Bluetooth POS printer shows
/// up in Windows as a virtual COM port (Bluetooth SPP), so this one transport covers Bluetooth and
/// USB-serial models alike — no vendor driver/SDK involved.</summary>
public class SerialReceiptPrinterService(ILogger<SerialReceiptPrinterService> logger) : IReceiptPrinterService
{
    public IReadOnlyList<string> ListPorts() => SerialPort.GetPortNames();

    public Task<PrintResult> PrintQrSlipAsync(bool[,] qrModules, ReceiptPrinterSettings settings, CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                if (string.IsNullOrWhiteSpace(settings.PortName))
                {
                    return new PrintResult(false, "Není nastaven COM port termotiskárny.");
                }

                try
                {
                    var payload = EscPosPayloadBuilder.Build(
                        settings.HeaderText, settings.FooterText, qrModules, settings.PaperWidthDots);

                    using var port = new SerialPort(settings.PortName, settings.BaudRate)
                    {
                        WriteTimeout = 10_000,
                    };
                    port.Open();
                    port.Write(payload, 0, payload.Length);
                    // Give the port's buffer a moment to drain before closing — Bluetooth SPP ports
                    // can silently drop the tail of the raster if closed immediately after Write.
                    Thread.Sleep(300);

                    logger.LogInformation("Printed QR slip ({Bytes} bytes) to {Port}.", payload.Length, settings.PortName);
                    return new PrintResult(true, null);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "QR slip print failed on port {Port}.", settings.PortName);
                    return new PrintResult(false, ex.Message);
                }
            },
            cancellationToken);
}
