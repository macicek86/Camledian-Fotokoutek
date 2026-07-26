using System.Text.Json;
using Camledian.Photobooth.Core.Models;
using Microsoft.Data.Sqlite;

namespace Camledian.Photobooth.Storage.Repositories;

public class EventRepository(SqliteConnectionFactory connectionFactory)
{
    public async Task UpsertAsync(EventDefinition ev, CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Events (Id, Name, OutputTemplateId, BackgroundAssetIdsJson, OverlayAssetIdsJson, IsActive)
            VALUES ($id, $name, $template, $backgrounds, $overlays, $active)
            ON CONFLICT(Id) DO UPDATE SET
                Name = excluded.Name,
                OutputTemplateId = excluded.OutputTemplateId,
                BackgroundAssetIdsJson = excluded.BackgroundAssetIdsJson,
                OverlayAssetIdsJson = excluded.OverlayAssetIdsJson,
                IsActive = excluded.IsActive
            """;
        command.Parameters.AddWithValue("$id", ev.Id);
        command.Parameters.AddWithValue("$name", ev.Name);
        command.Parameters.AddWithValue("$template", ev.OutputTemplateId);
        command.Parameters.AddWithValue("$backgrounds", JsonSerializer.Serialize(ev.BackgroundAssetIds));
        command.Parameters.AddWithValue("$overlays", JsonSerializer.Serialize(ev.OverlayAssetIds));
        command.Parameters.AddWithValue("$active", ev.IsActive ? 1 : 0);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<EventDefinition>> ListAsync(CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, OutputTemplateId, BackgroundAssetIdsJson, OverlayAssetIdsJson, IsActive FROM Events";
        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var results = new List<EventDefinition>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(Map(reader));
        }

        return results;
    }

    public async Task<EventDefinition?> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, OutputTemplateId, BackgroundAssetIdsJson, OverlayAssetIdsJson, IsActive FROM Events WHERE IsActive = 1 LIMIT 1";
        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Map(reader) : null;
    }

    private static EventDefinition Map(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        Name = reader.GetString(1),
        OutputTemplateId = reader.GetString(2),
        BackgroundAssetIds = JsonSerializer.Deserialize<List<string>>(reader.GetString(3)) ?? [],
        OverlayAssetIds = JsonSerializer.Deserialize<List<string>>(reader.GetString(4)) ?? [],
        IsActive = reader.GetInt32(5) != 0,
    };
}
