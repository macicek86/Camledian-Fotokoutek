namespace Camledian.Photobooth.Core.Models;

/// <summary>
/// Thermal receipt ("POS") printer for QR slips (spec §57-adjacent UX addition). Bluetooth models
/// pair in Windows and expose a virtual COM port (Bluetooth SPP); USB models usually install a
/// vendor COM port too. The app talks plain ESC/POS over that port — the de-facto standard for
/// 58/80mm receipt printers — so no vendor driver/SDK is needed.
/// </summary>
public class ReceiptPrinterSettings
{
    public bool Enabled { get; set; }

    /// <summary>Serial port name, e.g. "COM5" (check Windows Bluetooth settings > COM ports after
    /// pairing the printer).</summary>
    public string? PortName { get; set; }

    /// <summary>Common defaults: 9600 for most cheap BT models, some use 115200.</summary>
    public int BaudRate { get; set; } = 9600;

    /// <summary>Printable width in dots: 384 = 58mm paper (48mm printable @ 8 dots/mm),
    /// 576 = 80mm paper.</summary>
    public int PaperWidthDots { get; set; } = 384;

    /// <summary>Printed above the QR code. Czech diacritics are transliterated to ASCII before
    /// sending — receipt printers default to codepage 437 and would print garbage otherwise.</summary>
    public string HeaderText { get; set; } = "Vase fotografie";

    /// <summary>Printed below the QR code.</summary>
    public string FooterText { get; set; } = "Naskenujte QR kod mobilem";

    /// <summary>Print the slip automatically as soon as the photo finishes uploading (the QR is
    /// only known after upload). Manual printing from the result screen works either way.</summary>
    public bool AutoPrintReceipt { get; set; } = true;
}
