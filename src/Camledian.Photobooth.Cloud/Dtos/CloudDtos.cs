namespace Camledian.Photobooth.Cloud.Dtos;

// These mirror the JSON shapes returned by the Cloudflare Worker (cloud/src/routes/*.ts) exactly —
// keep the two in sync when either side's contract changes.

public sealed record PairStartResponse(string Code, DateTimeOffset ExpiresAt, int PollIntervalSeconds);

public sealed record PairStatusResponse(string Status, string? DeviceId, string? DeviceToken);

public sealed record EventSummaryDto(string Id, string Name, string OutputTemplateId);

public sealed record ConfigSyncDto(int SyncIntervalSeconds, int HeartbeatIntervalSeconds);

public sealed record ConfigResponse(string DeviceId, EventSummaryDto? Event, ConfigSyncDto Sync, string GalleryBaseUrl);

public sealed record AssetManifestEntryDto(string Id, string Type, string Name, string Hash, string Url);

public sealed record AssetManifestResponse(int Version, List<AssetManifestEntryDto> Assets);

public sealed record CreatePhotoResponse(
    string PhotoId,
    string UploadUrl,
    string Method,
    Dictionary<string, string> RequiredHeaders,
    int ExpiresInSeconds);

public sealed record CompleteUploadResponse(string PhotoId, string DownloadToken, string DownloadUrl);

public sealed record HeartbeatResponse(bool Ok, DateTimeOffset ServerTimeUtc);
