namespace Camledian.Photobooth.Core.Models;

/// <summary>Local record of this kiosk's cloud pairing (spec §36). The token is stored via
/// ICredentialStore, never in plain settings JSON.</summary>
public class DeviceRecord
{
    public required string DeviceId { get; init; }
    public required string DeviceToken { get; init; }
    public DateTimeOffset PairedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string? Name { get; set; }
}

public class AssetManifestEntry
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public required string Hash { get; init; }
    public required string Url { get; init; }
    public string? Name { get; init; }
}

public class AssetManifest
{
    public int Version { get; init; }
    public List<AssetManifestEntry> Assets { get; init; } = [];
}
