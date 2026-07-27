using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace Camledian.Photobooth.AI;

/// <summary>How far along a model download is, reported roughly 4x per MB so a progress bar moves
/// smoothly without flooding the UI dispatcher.</summary>
/// <param name="BytesReceived">Bytes written to disk so far.</param>
/// <param name="TotalBytes">Content-Length if the server sent one, otherwise the catalog estimate.</param>
/// <param name="Verifying">True once the bytes are all in and the SHA-256 is being checked — that
/// step is not instant on a 176 MB file, and without this the bar would sit at 100% looking hung.</param>
public sealed record AiModelDownloadProgress(long BytesReceived, long TotalBytes, bool Verifying);

/// <summary>
/// Downloads the <see cref="AiModelCatalog"/> models straight into the folder the app actually loads
/// them from, so an operator can fix "AI mode does nothing" from Admin instead of needing a shell,
/// PowerShell, and a rebuild.
///
/// The completeness guarantee is the whole point: bytes stream into a <c>.part</c> file while the
/// SHA-256 is computed incrementally, and the file is only moved onto the real path once that hash
/// matches the catalog. An interrupted or corrupted download therefore leaves nothing behind that
/// <see cref="AiBackgroundRemovalProvider"/> could mistake for a usable model — it would otherwise
/// fail deep inside ONNX Runtime with an opaque error instead of cleanly falling back to Green Screen.
/// </summary>
public sealed class AiModelDownloadService(HttpClient httpClient, ILogger<AiModelDownloadService> logger)
{
    private const int BufferSize = 128 * 1024;
    private const long ProgressIntervalBytes = 256 * 1024;

    /// <summary>True when a complete, hash-verified copy of <paramref name="model"/> is already at
    /// <paramref name="destinationPath"/>. Only checks existence and size — re-hashing 176 MB on
    /// every Admin screen open would freeze the UI; the hash is what guards the write itself.</summary>
    public static bool IsPresent(AiModelDescriptor model, string destinationPath)
    {
        var file = new FileInfo(destinationPath);
        return file.Exists && file.Length > 0;
    }

    /// <summary>Fetches one model and verifies it end to end. Throws on network failure, a truncated
    /// response, or a hash mismatch — in every case leaving <paramref name="destinationPath"/>
    /// untouched.</summary>
    public async Task DownloadAsync(
        AiModelDescriptor model,
        string destinationPath,
        IProgress<AiModelDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var partialPath = destinationPath + ".part";
        logger.LogInformation("Downloading AI model {FileName} from {Url}.", model.FileName, model.Url);

        try
        {
            using var response = await httpClient
                .GetAsync(model.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var declaredLength = response.Content.Headers.ContentLength;
            var totalBytes = declaredLength ?? model.ApproximateBytes;

            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var received = 0L;

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var destination = new FileStream(
                partialPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
            {
                var buffer = new byte[BufferSize];
                var lastReported = 0L;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    hasher.AppendData(buffer, 0, read);
                    received += read;

                    if (received - lastReported >= ProgressIntervalBytes)
                    {
                        lastReported = received;
                        progress?.Report(new AiModelDownloadProgress(received, Math.Max(totalBytes, received), Verifying: false));
                    }
                }
            }

            progress?.Report(new AiModelDownloadProgress(received, Math.Max(totalBytes, received), Verifying: true));

            // A silently truncated response (proxy timeout, connection reset mid-body) is the case
            // the hash alone would also catch, but this reports it in terms the operator can act on.
            if (declaredLength is { } expectedLength && received != expectedLength)
            {
                throw new IOException(
                    $"Stahování {model.FileName} skončilo předčasně: přijato {received} z {expectedLength} bajtů.");
            }

            var actualHash = Convert.ToHexStringLower(hasher.GetHashAndReset());
            if (!string.Equals(actualHash, model.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Kontrolní součet {model.FileName} nesouhlasí (očekáváno {model.Sha256}, staženo {actualHash}). " +
                    "Soubor je poškozený a nebyl použit.");
            }

            File.Move(partialPath, destinationPath, overwrite: true);
            logger.LogInformation(
                "AI model {FileName} downloaded and verified ({Bytes} bytes, sha256 {Hash}).",
                model.FileName, received, actualHash);
        }
        catch
        {
            TryDeletePartial(partialPath);
            throw;
        }
    }

    private void TryDeletePartial(string partialPath)
    {
        try
        {
            if (File.Exists(partialPath))
            {
                File.Delete(partialPath);
            }
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Could not clean up the partial download at {Path}.", partialPath);
        }
    }
}
