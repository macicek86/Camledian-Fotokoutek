using System.Net;
using System.Security.Cryptography;
using Camledian.Photobooth.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Camledian.Photobooth.Tests.AI;

public class AiModelDownloadServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "camledian-model-tests-" + Guid.NewGuid().ToString("N"));

    private string DestinationPath => Path.Combine(_directory, "model.onnx");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private static string Sha256Of(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static AiModelDescriptor Descriptor(byte[] payload, string? overrideHash = null) =>
        new("model.onnx", "https://example.invalid/model.onnx", overrideHash ?? Sha256Of(payload), payload.Length, Required: true);

    private static AiModelDownloadService ServiceReturning(
        byte[] body,
        HttpStatusCode status = HttpStatusCode.OK,
        long? declaredContentLength = null)
    {
        var handler = new StubHandler(body, status, declaredContentLength);
        return new AiModelDownloadService(new HttpClient(handler), NullLogger<AiModelDownloadService>.Instance);
    }

    [Fact]
    public async Task VerifiedDownloadLandsAtTheDestination()
    {
        var payload = RandomNumberGenerator.GetBytes(600_000);
        var service = ServiceReturning(payload);

        await service.DownloadAsync(Descriptor(payload), DestinationPath);

        Assert.True(File.Exists(DestinationPath));
        Assert.Equal(payload, await File.ReadAllBytesAsync(DestinationPath));
    }

    [Fact]
    public async Task CorruptedDownloadIsRejectedAndLeavesNoFileBehind()
    {
        var payload = RandomNumberGenerator.GetBytes(200_000);
        // Server hands back different bytes than the catalog's checksum describes.
        var service = ServiceReturning(RandomNumberGenerator.GetBytes(200_000));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.DownloadAsync(Descriptor(payload), DestinationPath));

        // The whole point: nothing that AiBackgroundRemovalProvider could mistake for a model.
        Assert.False(File.Exists(DestinationPath));
        Assert.False(File.Exists(DestinationPath + ".part"));
    }

    [Fact]
    public async Task TruncatedResponseIsRejected()
    {
        var payload = RandomNumberGenerator.GetBytes(100_000);
        // Content-Length promises the full file, the body stops short — a proxy timeout or reset.
        var service = ServiceReturning(payload[..40_000], declaredContentLength: payload.Length);

        await Assert.ThrowsAsync<IOException>(
            () => service.DownloadAsync(Descriptor(payload), DestinationPath));

        Assert.False(File.Exists(DestinationPath));
        Assert.False(File.Exists(DestinationPath + ".part"));
    }

    [Fact]
    public async Task HttpFailureLeavesNoFileBehind()
    {
        var payload = RandomNumberGenerator.GetBytes(1000);
        var service = ServiceReturning([], HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => service.DownloadAsync(Descriptor(payload), DestinationPath));

        Assert.False(File.Exists(DestinationPath));
        Assert.False(File.Exists(DestinationPath + ".part"));
    }

    [Fact]
    public async Task AnExistingGoodModelIsReplacedOnlyAfterVerification()
    {
        Directory.CreateDirectory(_directory);
        var existing = "already here"u8.ToArray();
        await File.WriteAllBytesAsync(DestinationPath, existing);

        var wanted = RandomNumberGenerator.GetBytes(50_000);
        var service = ServiceReturning(RandomNumberGenerator.GetBytes(50_000)); // wrong bytes

        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.DownloadAsync(Descriptor(wanted), DestinationPath));

        Assert.Equal(existing, await File.ReadAllBytesAsync(DestinationPath));
    }

    [Fact]
    public async Task ProgressReachesTheFullSizeAndReportsVerification()
    {
        var payload = RandomNumberGenerator.GetBytes(900_000);
        var service = ServiceReturning(payload, declaredContentLength: payload.Length);

        // Deliberately not Progress<T>: that posts through the synchronization context, so reports
        // would still be in flight when the assertions below run.
        var reports = new List<AiModelDownloadProgress>();
        await service.DownloadAsync(Descriptor(payload), DestinationPath, new CollectingProgress(reports));

        Assert.NotEmpty(reports);
        Assert.Contains(reports, r => r.Verifying);
        Assert.Equal(payload.Length, reports[^1].BytesReceived);
        Assert.All(reports, r => Assert.True(r.BytesReceived <= r.TotalBytes));
    }

    [Fact]
    public void CatalogEntriesAreMatchedByConfiguredFileName()
    {
        Assert.Equal(AiModelCatalog.PreviewModel, AiModelCatalog.FindByConfiguredPath("data/models/u2netp.onnx"));
        Assert.Equal(AiModelCatalog.FinalModel, AiModelCatalog.FindByConfiguredPath(@"C:\somewhere\u2net.onnx"));
        Assert.Null(AiModelCatalog.FindByConfiguredPath("data/models/my-own-model.onnx"));
        Assert.Null(AiModelCatalog.FindByConfiguredPath(""));
    }

    private sealed class CollectingProgress(List<AiModelDownloadProgress> sink) : IProgress<AiModelDownloadProgress>
    {
        public void Report(AiModelDownloadProgress value) => sink.Add(value);
    }

    private sealed class StubHandler(byte[] body, HttpStatusCode status, long? declaredContentLength) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(status) { Content = new ByteArrayContent(body) };
            if (declaredContentLength is { } length)
            {
                response.Content.Headers.ContentLength = length;
            }

            return Task.FromResult(response);
        }
    }
}
