using Camledian.Photobooth.Core.Models;
using Microsoft.Data.Sqlite;

namespace Camledian.Photobooth.Storage.Repositories;

public class AssetRepository(SqliteConnectionFactory connectionFactory)
{
    public async Task UpsertAsync(AssetRecord asset, CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Assets (Id, Type, Name, LocalPath, Hash, SourceUrl, SortOrder)
            VALUES ($id, $type, $name, $path, $hash, $source, $sort)
            ON CONFLICT(Id) DO UPDATE SET
                Type = excluded.Type,
                Name = excluded.Name,
                LocalPath = excluded.LocalPath,
                Hash = excluded.Hash,
                SourceUrl = excluded.SourceUrl,
                SortOrder = excluded.SortOrder
            """;
        command.Parameters.AddWithValue("$id", asset.Id);
        command.Parameters.AddWithValue("$type", asset.Type.ToString());
        command.Parameters.AddWithValue("$name", asset.Name);
        command.Parameters.AddWithValue("$path", asset.LocalPath);
        command.Parameters.AddWithValue("$hash", (object?)asset.Hash ?? DBNull.Value);
        command.Parameters.AddWithValue("$source", (object?)asset.SourceUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("$sort", asset.SortOrder);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AssetRecord>> ListAsync(AssetType type, CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Type, Name, LocalPath, Hash, SourceUrl, SortOrder FROM Assets WHERE Type = $type ORDER BY SortOrder";
        command.Parameters.AddWithValue("$type", type.ToString());
        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var results = new List<AssetRecord>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(Map(reader));
        }

        return results;
    }

    private static AssetRecord Map(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        Type = Enum.Parse<AssetType>(reader.GetString(1)),
        Name = reader.GetString(2),
        LocalPath = reader.GetString(3),
        Hash = reader.IsDBNull(4) ? null : reader.GetString(4),
        SourceUrl = reader.IsDBNull(5) ? null : reader.GetString(5),
        SortOrder = reader.GetInt32(6),
    };
}
