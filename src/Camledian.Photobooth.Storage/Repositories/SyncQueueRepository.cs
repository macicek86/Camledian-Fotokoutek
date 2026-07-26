using Camledian.Photobooth.Core.Models;
using Microsoft.Data.Sqlite;

namespace Camledian.Photobooth.Storage.Repositories;

/// <summary>Persistent upload queue (spec §38). Backed by SQLite so a pending upload survives an
/// app restart — the sync worker just resumes where it left off.</summary>
public class SyncQueueRepository(SqliteConnectionFactory connectionFactory)
{
    public async Task EnqueueAsync(SyncQueueItem item, CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO SyncQueue (Id, PhotoId, Status, Attempts, NextAttemptAtUtc, LastError, CreatedAtUtc)
            VALUES ($id, $photoId, $status, $attempts, $next, $error, $created)
            """;
        Bind(command, item);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(SyncQueueItem item, CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE SyncQueue SET Status = $status, Attempts = $attempts, NextAttemptAtUtc = $next, LastError = $error
            WHERE Id = $id
            """;
        Bind(command, item);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Backing store for the Diagnostics "Sync now" action (spec §44): makes every
    /// Failed/stalled item immediately due again instead of waiting out its backoff delay.</summary>
    public async Task ForceRetryAllAsync(CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE SyncQueue SET Status = 'Pending', NextAttemptAtUtc = $now WHERE Status IN ('Failed', 'Pending')";
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Items ready to be (re)attempted right now, oldest first.</summary>
    public async Task<IReadOnlyList<SyncQueueItem>> GetDueAsync(CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, PhotoId, Status, Attempts, NextAttemptAtUtc, LastError, CreatedAtUtc FROM SyncQueue
            WHERE Status IN ('Pending', 'Failed') AND NextAttemptAtUtc <= $now
            ORDER BY CreatedAtUtc
            """;
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var results = new List<SyncQueueItem>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(Map(reader));
        }

        return results;
    }

    public async Task<int> CountPendingAsync(CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM SyncQueue WHERE Status IN ('Pending', 'Failed', 'Uploading')";
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result);
    }

    private static void Bind(SqliteCommand command, SyncQueueItem item)
    {
        command.Parameters.AddWithValue("$id", item.Id);
        command.Parameters.AddWithValue("$photoId", item.PhotoId);
        command.Parameters.AddWithValue("$status", item.Status.ToString());
        command.Parameters.AddWithValue("$attempts", item.Attempts);
        command.Parameters.AddWithValue("$next", item.NextAttemptAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$error", (object?)item.LastError ?? DBNull.Value);
        command.Parameters.AddWithValue("$created", item.CreatedAtUtc.ToString("O"));
    }

    private static SyncQueueItem Map(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        PhotoId = reader.GetString(1),
        Status = Enum.Parse<SyncStatus>(reader.GetString(2)),
        Attempts = reader.GetInt32(3),
        NextAttemptAtUtc = DateTimeOffset.Parse(reader.GetString(4)),
        LastError = reader.IsDBNull(5) ? null : reader.GetString(5),
        CreatedAtUtc = DateTimeOffset.Parse(reader.GetString(6)),
    };
}
