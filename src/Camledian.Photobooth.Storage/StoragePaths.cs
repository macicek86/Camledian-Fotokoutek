namespace Camledian.Photobooth.Storage;

/// <summary>Resolves the relative paths in StorageSettings (e.g. "data/photos") against the app's
/// own base directory rather than the process's current working directory, which is not reliably
/// the exe's folder (a shortcut, a different launch cwd, a service host, ...).</summary>
public static class StoragePaths
{
    public static string Resolve(string path) =>
        Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
}
