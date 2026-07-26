namespace Camledian.Photobooth.Printing;

public sealed record PrinterInfo(string Name, bool IsDefault);

public sealed record PrintResult(bool Success, string? ErrorMessage);

/// <summary>
/// Windows printing (spec §28). A failed print must never lose the photo — CaptureAsync has already
/// saved it to disk before Print is ever called, so PrintResult.Success = false just means "let the
/// user retry", not "redo the shoot".
/// </summary>
public interface IPrintingService
{
    IReadOnlyList<PrinterInfo> ListPrinters();

    Task<PrintResult> PrintAsync(string imagePath, Core.Models.PrintSettings settings, CancellationToken cancellationToken = default);
}
