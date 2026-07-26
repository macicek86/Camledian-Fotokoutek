namespace Camledian.Photobooth.Core.Models;

/// <summary>
/// Root settings aggregate, persisted to SQLite (Settings table, one row per key) and mirrored to
/// an in-memory instance the admin screen edits live. See SettingsRepository in Storage.
/// </summary>
public class AppSettings
{
    public CameraSettings Camera { get; set; } = new();
    public ChromaKeySettings ChromaKey { get; set; } = new();
    public BackgroundSubtractionSettings BackgroundSubtraction { get; set; } = new();
    public AiSettings Ai { get; set; } = new();
    public PrintSettings Print { get; set; } = new();
    public CloudSettings Cloud { get; set; } = new();
    public UiSettings Ui { get; set; } = new();
    public StorageSettings Storage { get; set; } = new();
}
