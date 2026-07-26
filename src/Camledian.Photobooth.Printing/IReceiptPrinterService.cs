using Camledian.Photobooth.Core.Models;

namespace Camledian.Photobooth.Printing;

/// <summary>QR-slip printing on a thermal receipt printer over a serial (COM) port — which is what
/// paired Bluetooth POS printers show up as on Windows. Failure is always a soft PrintResult, never
/// an exception up into the capture flow: a missing slip must not disturb the photo session.</summary>
public interface IReceiptPrinterService
{
    IReadOnlyList<string> ListPorts();

    Task<PrintResult> PrintQrSlipAsync(bool[,] qrModules, ReceiptPrinterSettings settings, CancellationToken cancellationToken = default);
}
