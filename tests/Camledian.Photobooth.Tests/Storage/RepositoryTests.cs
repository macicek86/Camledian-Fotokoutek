using Camledian.Photobooth.Core.Models;
using Camledian.Photobooth.Storage.Repositories;

namespace Camledian.Photobooth.Tests.Storage;

public class RepositoryTests : IClassFixture<TempDatabaseFixture>
{
    private readonly TempDatabaseFixture _fixture;

    public RepositoryTests(TempDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task SettingsRepository_RoundTripsASection()
    {
        var repo = new SettingsRepository(_fixture.ConnectionFactory);
        var chromaKey = new ChromaKeySettings { TargetHueDegrees = 42, FeatherPixels = 7 };

        await repo.SaveSectionAsync(nameof(AppSettings.ChromaKey), chromaKey);
        var loaded = await repo.LoadAsync();

        Assert.Equal(42, loaded.ChromaKey.TargetHueDegrees);
        Assert.Equal(7, loaded.ChromaKey.FeatherPixels);
    }

    [Fact]
    public async Task SettingsRepository_LoadAsync_ReturnsDefaultsWhenEmpty()
    {
        using var emptyFixture = new TempDatabaseFixture();
        var repo = new SettingsRepository(emptyFixture.ConnectionFactory);

        var loaded = await repo.LoadAsync();

        Assert.Equal(new AppSettings().Ui.CountdownSeconds, loaded.Ui.CountdownSeconds);
    }

    [Fact]
    public async Task EventRepository_UpsertThenGetActive()
    {
        var repo = new EventRepository(_fixture.ConnectionFactory);
        var ev = new EventDefinition
        {
            Id = "evt-1",
            Name = "Demo",
            BackgroundAssetIds = ["bg-1", "bg-2"],
            IsActive = true,
        };

        await repo.UpsertAsync(ev);
        var active = await repo.GetActiveAsync();

        Assert.NotNull(active);
        Assert.Equal("Demo", active!.Name);
        Assert.Equal(2, active.BackgroundAssetIds.Count);
    }

    [Fact]
    public async Task AssetRepository_ListsByTypeInSortOrder()
    {
        var repo = new AssetRepository(_fixture.ConnectionFactory);
        await repo.UpsertAsync(new AssetRecord { Id = "b2", Type = AssetType.Background, Name = "B2", LocalPath = "/b2.jpg", SortOrder = 1 });
        await repo.UpsertAsync(new AssetRecord { Id = "b1", Type = AssetType.Background, Name = "B1", LocalPath = "/b1.jpg", SortOrder = 0 });
        await repo.UpsertAsync(new AssetRecord { Id = "o1", Type = AssetType.Overlay, Name = "O1", LocalPath = "/o1.png", SortOrder = 0 });

        var backgrounds = await repo.ListAsync(AssetType.Background);

        Assert.Equal(2, backgrounds.Count);
        Assert.Equal("b1", backgrounds[0].Id);
        Assert.Equal("b2", backgrounds[1].Id);
    }

    [Fact]
    public async Task PhotoRepository_InsertThenGetById()
    {
        var repo = new PhotoRepository(_fixture.ConnectionFactory);
        var photo = new PhotoRecord
        {
            Id = "photo-1",
            EventId = "evt-1",
            OriginalPath = "/data/originals/photo-1.jpg",
            FinalPath = "/data/final/photo-1.jpg",
        };

        await repo.InsertAsync(photo);
        var loaded = await repo.GetByIdAsync("photo-1");

        Assert.NotNull(loaded);
        Assert.False(loaded!.Synced);
        Assert.Equal("/data/final/photo-1.jpg", loaded.FinalPath);
    }

    [Fact]
    public async Task PhotoRepository_UpdateMarksSyncedAndListUnsyncedExcludesIt()
    {
        var repo = new PhotoRepository(_fixture.ConnectionFactory);
        var photo = new PhotoRecord
        {
            Id = "photo-2",
            EventId = "evt-1",
            OriginalPath = "/o.jpg",
            FinalPath = "/f.jpg",
        };
        await repo.InsertAsync(photo);

        var unsyncedBefore = await repo.ListUnsyncedAsync();
        Assert.Contains(unsyncedBefore, p => p.Id == "photo-2");

        photo.Synced = true;
        photo.CloudPhotoId = "cloud-1";
        photo.DownloadToken = "tok";
        photo.DownloadUrl = "https://example.com/foto/tok";
        await repo.UpdateAsync(photo);

        var unsyncedAfter = await repo.ListUnsyncedAsync();
        Assert.DoesNotContain(unsyncedAfter, p => p.Id == "photo-2");

        var reloaded = await repo.GetByIdAsync("photo-2");
        Assert.True(reloaded!.Synced);
        Assert.Equal("cloud-1", reloaded.CloudPhotoId);
    }

    [Fact]
    public async Task DeviceRepository_SaveThenGetCurrent()
    {
        var repo = new DeviceRepository(_fixture.ConnectionFactory);
        await repo.SaveAsync(new DeviceRecord { DeviceId = "dev-1", DeviceToken = "secret-token", Name = "Kiosk 1" });

        var current = await repo.GetCurrentAsync();

        Assert.NotNull(current);
        Assert.Equal("dev-1", current!.DeviceId);
        Assert.Equal("secret-token", current.DeviceToken);
    }
}
