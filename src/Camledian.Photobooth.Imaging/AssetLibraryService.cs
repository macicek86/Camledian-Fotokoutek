using System.Security.Cryptography;
using Camledian.Photobooth.Core.Models;

namespace Camledian.Photobooth.Imaging;

/// <summary>Scans the local backgrounds/overlays folders (spec §11/§13) and produces AssetRecords
/// with a content hash, so CloudSyncService can later diff against the cloud manifest (spec §37)
/// without re-downloading unchanged files.</summary>
public class AssetLibraryService
{
    private static readonly string[] SupportedExtensions = [".png", ".jpg", ".jpeg", ".webp"];

    public IReadOnlyList<AssetRecord> ScanDirectory(string directoryPath, AssetType type)
    {
        if (!Directory.Exists(directoryPath))
        {
            return [];
        }

        var results = new List<AssetRecord>();
        var files = Directory.GetFiles(directoryPath)
            .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i];
            var id = Path.GetFileNameWithoutExtension(file);
            results.Add(new AssetRecord
            {
                Id = id,
                Type = type,
                Name = id.Replace('-', ' ').Replace('_', ' '),
                LocalPath = file,
                Hash = ComputeHash(file),
                SortOrder = i,
            });
        }

        return results;
    }

    public static string ComputeHash(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexStringLower(hash);
    }
}
