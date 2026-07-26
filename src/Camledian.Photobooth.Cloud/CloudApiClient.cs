using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Camledian.Photobooth.Cloud.Dtos;

namespace Camledian.Photobooth.Cloud;

/// <summary>
/// Thin typed HTTP client for the Cloudflare Worker API (spec §32). Every method mirrors one Worker
/// route 1:1 — see cloud/src/routes/*.ts for the server side of each contract.
/// </summary>
public class CloudApiClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<PairStartResponse> PairStartAsync(string code, CancellationToken cancellationToken = default)
    {
        var response = await httpClient
            .PostAsJsonAsync("/api/photobooth/pair/start", new { code }, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<PairStartResponse>(JsonOptions, cancellationToken).ConfigureAwait(false))!;
    }

    public async Task<PairStatusResponse> PairStatusAsync(string code, CancellationToken cancellationToken = default)
    {
        var response = await httpClient
            .GetAsync($"/api/photobooth/pair/status/{Uri.EscapeDataString(code)}", cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<PairStatusResponse>(JsonOptions, cancellationToken).ConfigureAwait(false))!;
    }

    public async Task<ConfigResponse> GetConfigAsync(string deviceToken, CancellationToken cancellationToken = default)
    {
        using var request = NewAuthedRequest(HttpMethod.Get, "/api/photobooth/config", deviceToken);
        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<ConfigResponse>(JsonOptions, cancellationToken).ConfigureAwait(false))!;
    }

    public async Task<AssetManifestResponse> GetEventAssetsAsync(string deviceToken, string eventId, CancellationToken cancellationToken = default)
    {
        using var request = NewAuthedRequest(HttpMethod.Get, $"/api/photobooth/events/{eventId}/assets", deviceToken);
        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<AssetManifestResponse>(JsonOptions, cancellationToken).ConfigureAwait(false))!;
    }

    public async Task<CreatePhotoResponse> CreatePhotoAsync(string deviceToken, string contentType, string? eventId, CancellationToken cancellationToken = default)
    {
        using var request = NewAuthedRequest(HttpMethod.Post, "/api/photobooth/photos", deviceToken);
        request.Content = JsonContent.Create(new { eventId, contentType }, options: JsonOptions);
        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<CreatePhotoResponse>(JsonOptions, cancellationToken).ConfigureAwait(false))!;
    }

    public async Task<CompleteUploadResponse> CompleteUploadAsync(string deviceToken, string photoId, CancellationToken cancellationToken = default)
    {
        using var request = NewAuthedRequest(HttpMethod.Post, $"/api/photobooth/photos/{photoId}/upload-complete", deviceToken);
        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<CompleteUploadResponse>(JsonOptions, cancellationToken).ConfigureAwait(false))!;
    }

    public async Task<HeartbeatResponse> HeartbeatAsync(string deviceToken, string status, CancellationToken cancellationToken = default)
    {
        using var request = NewAuthedRequest(HttpMethod.Post, "/api/photobooth/heartbeat", deviceToken);
        request.Content = JsonContent.Create(new { status }, options: JsonOptions);
        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<HeartbeatResponse>(JsonOptions, cancellationToken).ConfigureAwait(false))!;
    }

    /// <summary>PUTs straight to R2 via the presigned URL from <see cref="CreatePhotoAsync"/> — this
    /// traffic bypasses the Worker entirely (spec §34), so it deliberately does not go through
    /// <paramref name="httpClient"/>'s base address/auth pipeline.</summary>
    public static async Task UploadToPresignedUrlAsync(
        string presignedUrl,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        using var uploadClient = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Put, presignedUrl)
        {
            Content = new StreamContent(content),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        var response = await uploadClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private static HttpRequestMessage NewAuthedRequest(HttpMethod method, string relativeUrl, string deviceToken)
    {
        var request = new HttpRequestMessage(method, relativeUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", deviceToken);
        return request;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        throw new CloudApiException(response.StatusCode, body);
    }
}

public sealed class CloudApiException(System.Net.HttpStatusCode statusCode, string responseBody)
    : Exception($"Cloud API request failed with {(int)statusCode} {statusCode}: {responseBody}")
{
    public System.Net.HttpStatusCode StatusCode { get; } = statusCode;
}
