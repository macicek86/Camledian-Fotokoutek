using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Camledian.Photobooth.Cloud.Dtos;

namespace Camledian.Photobooth.Cloud;

/// <summary>
/// Thin typed HTTP client for the Cloudflare Worker API (spec §32). Every method mirrors one Worker
/// route 1:1 — see cloud/src/routes/*.ts for the server side of each contract.
/// </summary>
/// <remarks>
/// <paramref name="baseUrlProvider"/> is re-read on every request instead of being baked into
/// <paramref name="httpClient"/>'s <c>BaseAddress</c> once: .NET forbids changing
/// <c>HttpClient.BaseAddress</c> after the first request is sent, which previously meant an admin
/// editing the Cloud API URL and saving had no effect until the app was restarted.
/// </remarks>
public class CloudApiClient(HttpClient httpClient, Func<string> baseUrlProvider)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<PairStartResponse> PairStartAsync(string code, CancellationToken cancellationToken = default)
    {
        var response = await httpClient
            .PostAsJsonAsync(BuildUri("/api/photobooth/pair/start"), new { code }, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<PairStartResponse>(JsonOptions, cancellationToken).ConfigureAwait(false))!;
    }

    public async Task<PairStatusResponse> PairStatusAsync(string code, CancellationToken cancellationToken = default)
    {
        var response = await httpClient
            .GetAsync(BuildUri($"/api/photobooth/pair/status/{Uri.EscapeDataString(code)}"), cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<PairStatusResponse>(JsonOptions, cancellationToken).ConfigureAwait(false))!;
    }

    public async Task<ConfigResponse> GetConfigAsync(string deviceToken, CancellationToken cancellationToken = default)
    {
        using var request = NewAuthedRequest(HttpMethod.Get, BuildUri("/api/photobooth/config"), deviceToken);
        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<ConfigResponse>(JsonOptions, cancellationToken).ConfigureAwait(false))!;
    }

    public async Task<AssetManifestResponse> GetEventAssetsAsync(string deviceToken, string eventId, CancellationToken cancellationToken = default)
    {
        using var request = NewAuthedRequest(HttpMethod.Get, BuildUri($"/api/photobooth/events/{eventId}/assets"), deviceToken);
        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<AssetManifestResponse>(JsonOptions, cancellationToken).ConfigureAwait(false))!;
    }

    public async Task<CreatePhotoResponse> CreatePhotoAsync(string deviceToken, string contentType, string? eventId, CancellationToken cancellationToken = default)
    {
        using var request = NewAuthedRequest(HttpMethod.Post, BuildUri("/api/photobooth/photos"), deviceToken);
        request.Content = JsonContent.Create(new { eventId, contentType }, options: JsonOptions);
        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<CreatePhotoResponse>(JsonOptions, cancellationToken).ConfigureAwait(false))!;
    }

    public async Task<CompleteUploadResponse> CompleteUploadAsync(string deviceToken, string photoId, CancellationToken cancellationToken = default)
    {
        using var request = NewAuthedRequest(HttpMethod.Post, BuildUri($"/api/photobooth/photos/{photoId}/upload-complete"), deviceToken);
        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<CompleteUploadResponse>(JsonOptions, cancellationToken).ConfigureAwait(false))!;
    }

    public async Task<HeartbeatResponse> HeartbeatAsync(string deviceToken, string status, CancellationToken cancellationToken = default)
    {
        using var request = NewAuthedRequest(HttpMethod.Post, BuildUri("/api/photobooth/heartbeat"), deviceToken);
        request.Content = JsonContent.Create(new { status }, options: JsonOptions);
        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<HeartbeatResponse>(JsonOptions, cancellationToken).ConfigureAwait(false))!;
    }

    private Uri BuildUri(string relativeUrl) =>
        new(new Uri(baseUrlProvider().TrimEnd('/') + "/"), relativeUrl.TrimStart('/'));

    /// <summary>PUTs the photo bytes to the Worker-hosted upload URL from <see cref="CreatePhotoAsync"/>
    /// (spec §34) — the Worker streams them straight into R2 via its own binding, so this goes
    /// through the normal authed base-address pipeline like every other call here, unlike a
    /// presigned-URL upload which would bypass the Worker (and need separate R2 API credentials).</summary>
    public async Task UploadPhotoBytesAsync(
        string deviceToken,
        string uploadUrl,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        using var request = NewAuthedRequest(HttpMethod.Put, BuildUri(uploadUrl), deviceToken);
        request.Content = new StreamContent(content);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private static HttpRequestMessage NewAuthedRequest(HttpMethod method, Uri requestUri, string deviceToken)
    {
        var request = new HttpRequestMessage(method, requestUri);
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
