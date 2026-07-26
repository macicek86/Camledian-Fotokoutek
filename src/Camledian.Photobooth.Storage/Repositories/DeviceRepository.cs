using Camledian.Photobooth.Core.Models;
using Microsoft.Data.Sqlite;

namespace Camledian.Photobooth.Storage.Repositories;

/// <summary>The kiosk only ever has one paired cloud identity at a time, so this is deliberately a
/// singleton "current device" accessor rather than a general multi-row CRUD API.</summary>
public class DeviceRepository(SqliteConnectionFactory connectionFactory)
{
    public async Task<DeviceRecord?> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT DeviceId, DeviceToken, Name, PairedAtUtc FROM Devices ORDER BY PairedAtUtc DESC LIMIT 1";
        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new DeviceRecord
        {
            DeviceId = reader.GetString(0),
            DeviceToken = reader.GetString(1),
            Name = reader.IsDBNull(2) ? null : reader.GetString(2),
            PairedAtUtc = DateTimeOffset.Parse(reader.GetString(3)),
        };
    }

    public async Task SaveAsync(DeviceRecord device, CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Devices (DeviceId, DeviceToken, Name, PairedAtUtc) VALUES ($id, $token, $name, $paired)
            ON CONFLICT(DeviceId) DO UPDATE SET DeviceToken = excluded.DeviceToken, Name = excluded.Name, PairedAtUtc = excluded.PairedAtUtc
            """;
        command.Parameters.AddWithValue("$id", device.DeviceId);
        command.Parameters.AddWithValue("$token", device.DeviceToken);
        command.Parameters.AddWithValue("$name", (object?)device.Name ?? DBNull.Value);
        command.Parameters.AddWithValue("$paired", device.PairedAtUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
