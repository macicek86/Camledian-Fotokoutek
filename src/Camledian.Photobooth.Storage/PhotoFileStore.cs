using Camledian.Photobooth.Core.Models;

namespace Camledian.Photobooth.Storage;

/// <summary>
/// Local photo file layout per spec §16:
/// photos/&lt;yyyy-MM-dd&gt;/originals/&lt;uuid&gt;.jpg and photos/&lt;yyyy-MM-dd&gt;/final/&lt;uuid&gt;.jpg
/// </summary>
public class PhotoFileStore(StorageSettings settings)
{
    public string GetOriginalPath(string photoId, DateTimeOffset date, string extension = "jpg") =>
        GetPath(photoId, date, "originals", extension);

    public string GetFinalPath(string photoId, DateTimeOffset date, string extension = "jpg") =>
        GetPath(photoId, date, "final", extension);

    private string GetPath(string photoId, DateTimeOffset date, string subfolder, string extension)
    {
        var dayDirectory = Path.Combine(
            StoragePaths.Resolve(settings.PhotosDirectory),
            date.UtcDateTime.ToString("yyyy-MM-dd"),
            subfolder);
        Directory.CreateDirectory(dayDirectory);
        return Path.Combine(dayDirectory, $"{photoId}.{extension.TrimStart('.')}");
    }
}
