using System.IO;
using Camledian.Photobooth.Core.Models;
using Camledian.Photobooth.Imaging;
using Camledian.Photobooth.Storage;
using Camledian.Photobooth.Storage.Repositories;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Camledian.Photobooth.App.Services;

/// <summary>Scans the bundled assets/backgrounds and assets/overlays folders (spec §11/§13), seeds
/// the demo event on first run, and loads the actual pixel data on demand for compositing.</summary>
public class AssetCatalogService(EventRepository eventRepository, AssetRepository assetRepository)
{
    private readonly AssetLibraryService _scanner = new();

    public IReadOnlyList<AssetRecord> Backgrounds { get; private set; } = [];
    public IReadOnlyList<AssetRecord> Overlays { get; private set; } = [];

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var baseDirectory = AppContext.BaseDirectory;
        Backgrounds = _scanner.ScanDirectory(Path.Combine(baseDirectory, "assets", "backgrounds"), AssetType.Background);
        Overlays = _scanner.ScanDirectory(Path.Combine(baseDirectory, "assets", "overlays"), AssetType.Overlay);

        var seeder = new DemoDataSeeder(eventRepository, assetRepository);
        await seeder.SeedIfEmptyAsync(Backgrounds, Overlays, cancellationToken).ConfigureAwait(false);
    }

    public static Image<Rgba32> LoadImage(AssetRecord asset) => Image.Load<Rgba32>(asset.LocalPath);
}
