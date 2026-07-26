namespace Camledian.Photobooth.Core.Models;

public class StorageSettings
{
    /// <summary>Root data directory. Relative paths are resolved against the app base directory.</summary>
    public string DataDirectory { get; set; } = "data";

    public string EventsDirectory { get; set; } = "data/events";
    public string PhotosDirectory { get; set; } = "data/photos";
    public string LogsDirectory { get; set; } = "data/logs";
    public string ModelsDirectory { get; set; } = "data/models";
    public string CacheDirectory { get; set; } = "data/cache";
    public string DatabaseFile { get; set; } = "data/photobooth.db";

    public double MinFreeDiskSpaceWarningGb { get; set; } = 2.0;
}
