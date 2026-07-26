using Camledian.Photobooth.Storage;
using Camledian.Photobooth.Storage.Migrations;

namespace Camledian.Photobooth.Tests.Storage;

/// <summary>A fresh, migrated SQLite database file per test class instance, cleaned up afterwards.</summary>
public sealed class TempDatabaseFixture : IDisposable
{
    private readonly string _dbPath;

    public TempDatabaseFixture()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"camledian-tests-{Guid.NewGuid():N}.db");
        ConnectionFactory = new SqliteConnectionFactory(_dbPath);
        using var connection = ConnectionFactory.Create();
        DbMigrator.Migrate(connection);
    }

    public SqliteConnectionFactory ConnectionFactory { get; }

    public void Dispose()
    {
        foreach (var suffix in new[] { "", "-shm", "-wal" })
        {
            var path = _dbPath + suffix;
            if (File.Exists(path))
            {
                try
                {
                    File.Delete(path);
                }
                catch (IOException)
                {
                    // best-effort cleanup
                }
            }
        }
    }
}
