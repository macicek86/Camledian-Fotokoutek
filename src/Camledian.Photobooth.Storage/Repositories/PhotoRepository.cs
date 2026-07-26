using Camledian.Photobooth.Core.Models;
using Microsoft.Data.Sqlite;

namespace Camledian.Photobooth.Storage.Repositories;

public class PhotoRepository(SqliteConnectionFactory connectionFactory)
{
    public async Task InsertAsync(PhotoRecord photo, CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Photos (Id, EventId, OriginalPath, FinalPath, CreatedAtUtc, Printed, PrintAttempts, Synced, CloudPhotoId, DownloadToken, DownloadUrl)
            VALUES ($id, $eventId, $original, $final, $created, $printed, $printAttempts, $synced, $cloudId, $token, $url)
            """;
        BindPhoto(command, photo);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(PhotoRecord photo, CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Photos SET
                EventId = $eventId, OriginalPath = $original, FinalPath = $final, CreatedAtUtc = $created,
                Printed = $printed, PrintAttempts = $printAttempts, Synced = $synced,
                CloudPhotoId = $cloudId, DownloadToken = $token, DownloadUrl = $url
            WHERE Id = $id
            """;
        BindPhoto(command, photo);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<PhotoRecord?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = SelectSql + " WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Map(reader) : null;
    }

    public async Task<IReadOnlyList<PhotoRecord>> ListUnsyncedAsync(CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = SelectSql + " WHERE Synced = 0 ORDER BY CreatedAtUtc";
        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var results = new List<PhotoRecord>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(Map(reader));
        }

        return results;
    }

    private const string SelectSql =
        "SELECT Id, EventId, OriginalPath, FinalPath, CreatedAtUtc, Printed, PrintAttempts, Synced, CloudPhotoId, DownloadToken, DownloadUrl FROM Photos";

    private static void BindPhoto(SqliteCommand command, PhotoRecord photo)
    {
        command.Parameters.AddWithValue("$id", photo.Id);
        command.Parameters.AddWithValue("$eventId", photo.EventId);
        command.Parameters.AddWithValue("$original", photo.OriginalPath);
        command.Parameters.AddWithValue("$final", photo.FinalPath);
        command.Parameters.AddWithValue("$created", photo.CreatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$printed", photo.Printed ? 1 : 0);
        command.Parameters.AddWithValue("$printAttempts", photo.PrintAttempts);
        command.Parameters.AddWithValue("$synced", photo.Synced ? 1 : 0);
        command.Parameters.AddWithValue("$cloudId", (object?)photo.CloudPhotoId ?? DBNull.Value);
        command.Parameters.AddWithValue("$token", (object?)photo.DownloadToken ?? DBNull.Value);
        command.Parameters.AddWithValue("$url", (object?)photo.DownloadUrl ?? DBNull.Value);
    }

    private static PhotoRecord Map(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        EventId = reader.GetString(1),
        OriginalPath = reader.GetString(2),
        FinalPath = reader.GetString(3),
        CreatedAtUtc = DateTimeOffset.Parse(reader.GetString(4)),
        Printed = reader.GetInt32(5) != 0,
        PrintAttempts = reader.GetInt32(6),
        Synced = reader.GetInt32(7) != 0,
        CloudPhotoId = reader.IsDBNull(8) ? null : reader.GetString(8),
        DownloadToken = reader.IsDBNull(9) ? null : reader.GetString(9),
        DownloadUrl = reader.IsDBNull(10) ? null : reader.GetString(10),
    };
}
