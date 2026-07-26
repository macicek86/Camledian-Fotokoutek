using Camledian.Photobooth.Core.Models;
using Camledian.Photobooth.Storage.Migrations;

namespace Camledian.Photobooth.Storage;

/// <summary>Single entry point that makes the data/ directory tree and photobooth.db exist and be
/// up to date, called once at app startup (spec §17: "DB musí být inicializována automaticky").</summary>
public static class StorageInitializer
{
    public static void Initialize(StorageSettings settings, SqliteConnectionFactory connectionFactory)
    {
        Directory.CreateDirectory(StoragePaths.Resolve(settings.DataDirectory));
        Directory.CreateDirectory(StoragePaths.Resolve(settings.EventsDirectory));
        Directory.CreateDirectory(StoragePaths.Resolve(settings.PhotosDirectory));
        Directory.CreateDirectory(StoragePaths.Resolve(settings.LogsDirectory));
        Directory.CreateDirectory(StoragePaths.Resolve(settings.ModelsDirectory));
        Directory.CreateDirectory(StoragePaths.Resolve(settings.CacheDirectory));

        using var connection = connectionFactory.Create();
        DbMigrator.Migrate(connection);
    }
}
