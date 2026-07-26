namespace Camledian.Photobooth.Core.Models;

public enum AssetType
{
    Background,
    Overlay,
}

/// <summary>A background or overlay image known to the app, either bundled locally or synced from
/// the cloud manifest (see CloudSyncService / manifest.json in spec section 37).</summary>
public class AssetRecord
{
    public required string Id { get; init; }
    public required AssetType Type { get; init; }
    public required string Name { get; init; }
    public required string LocalPath { get; init; }
    public string? Hash { get; set; }
    public string? SourceUrl { get; set; }
    public int SortOrder { get; set; }
}

public class EventDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public List<string> BackgroundAssetIds { get; init; } = [];
    public List<string> OverlayAssetIds { get; init; } = [];
    public string OutputTemplateId { get; set; } = OutputTemplate.DigitalLandscape.Id;
    public bool IsActive { get; set; } = true;
}
