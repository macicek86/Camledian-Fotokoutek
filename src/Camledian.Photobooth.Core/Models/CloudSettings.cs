namespace Camledian.Photobooth.Core.Models;

public class CloudSettings
{
    public bool Enabled { get; set; } = false;
    // fotokoutek.camledian.art is the Worker's custom domain (cloud/wrangler.toml routes) and serves
    // both the API and the gallery — the old workers.dev URL below still works but is the raw
    // Workers.dev subdomain, not the production domain admins expect to see/enter.
    public string ApiBaseUrl { get; set; } = "https://fotokoutek.camledian.art";
    public string? DeviceId { get; set; }
    public string? DeviceToken { get; set; }
    public int SyncIntervalSeconds { get; set; } = 60;
    public int HeartbeatIntervalSeconds { get; set; } = 30;
    public int UploadRetryBaseDelaySeconds { get; set; } = 5;
    public int UploadRetryMaxDelaySeconds { get; set; } = 900;
    public int UploadMaxAttempts { get; set; } = 10;
    // fotokoutek.camledian.art is a dedicated subdomain for this Worker — camledian.art itself
    // already routes to a separate (e-shop) Worker, so this avoids any route-pattern collision.
    public string GalleryBaseUrl { get; set; } = "https://fotokoutek.camledian.art/foto";
}
