namespace Camledian.Photobooth.Storage.Migrations;

/// <summary>
/// Hand-rolled schema versioning (spec §17: "Použij migrations nebo vlastní verzování schema").
/// Each entry runs at most once, tracked in SchemaMigrations, in a single transaction each — enough
/// for a single-writer kiosk SQLite database without pulling in a full migration framework.
/// </summary>
public static class DbMigrator
{
    private static readonly (int Version, string Sql)[] Migrations =
    [
        (1, """
            CREATE TABLE IF NOT EXISTS Settings (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Events (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                OutputTemplateId TEXT NOT NULL,
                BackgroundAssetIdsJson TEXT NOT NULL DEFAULT '[]',
                OverlayAssetIdsJson TEXT NOT NULL DEFAULT '[]',
                IsActive INTEGER NOT NULL DEFAULT 1
            );

            CREATE TABLE IF NOT EXISTS Assets (
                Id TEXT PRIMARY KEY,
                Type TEXT NOT NULL,
                Name TEXT NOT NULL,
                LocalPath TEXT NOT NULL,
                Hash TEXT,
                SourceUrl TEXT,
                SortOrder INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS Photos (
                Id TEXT PRIMARY KEY,
                EventId TEXT NOT NULL,
                OriginalPath TEXT NOT NULL,
                FinalPath TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                Printed INTEGER NOT NULL DEFAULT 0,
                PrintAttempts INTEGER NOT NULL DEFAULT 0,
                Synced INTEGER NOT NULL DEFAULT 0,
                CloudPhotoId TEXT,
                DownloadToken TEXT,
                DownloadUrl TEXT
            );
            CREATE INDEX IF NOT EXISTS IX_Photos_EventId ON Photos(EventId);

            CREATE TABLE IF NOT EXISTS SyncQueue (
                Id TEXT PRIMARY KEY,
                PhotoId TEXT NOT NULL,
                Status TEXT NOT NULL,
                Attempts INTEGER NOT NULL DEFAULT 0,
                NextAttemptAtUtc TEXT NOT NULL,
                LastError TEXT,
                CreatedAtUtc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_SyncQueue_Status ON SyncQueue(Status, NextAttemptAtUtc);

            CREATE TABLE IF NOT EXISTS Devices (
                DeviceId TEXT PRIMARY KEY,
                DeviceToken TEXT NOT NULL,
                Name TEXT,
                PairedAtUtc TEXT NOT NULL
            );
            """),
    ];

    public static void Migrate(Microsoft.Data.Sqlite.SqliteConnection connection)
    {
        using (var createTable = connection.CreateCommand())
        {
            createTable.CommandText = """
                CREATE TABLE IF NOT EXISTS SchemaMigrations (
                    Version INTEGER PRIMARY KEY,
                    AppliedAtUtc TEXT NOT NULL
                );
                """;
            createTable.ExecuteNonQuery();
        }

        var applied = new HashSet<int>();
        using (var query = connection.CreateCommand())
        {
            query.CommandText = "SELECT Version FROM SchemaMigrations";
            using var reader = query.ExecuteReader();
            while (reader.Read())
            {
                applied.Add(reader.GetInt32(0));
            }
        }

        foreach (var (version, sql) in Migrations.OrderBy(m => m.Version))
        {
            if (applied.Contains(version))
            {
                continue;
            }

            using var transaction = connection.BeginTransaction();
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }

            using (var record = connection.CreateCommand())
            {
                record.Transaction = transaction;
                record.CommandText = "INSERT INTO SchemaMigrations (Version, AppliedAtUtc) VALUES ($version, $now)";
                record.Parameters.AddWithValue("$version", version);
                record.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
                record.ExecuteNonQuery();
            }

            transaction.Commit();
        }
    }
}
