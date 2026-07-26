namespace Camledian.Photobooth.Core.Models;

public class PhotoRecord
{
    public required string Id { get; init; }
    public required string EventId { get; init; }
    public required string OriginalPath { get; init; }
    public required string FinalPath { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public bool Printed { get; set; }
    public int PrintAttempts { get; set; }

    public bool Synced { get; set; }
    public string? CloudPhotoId { get; set; }
    public string? DownloadToken { get; set; }
    public string? DownloadUrl { get; set; }
}
