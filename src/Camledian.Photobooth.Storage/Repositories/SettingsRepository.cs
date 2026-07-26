using System.Text.Json;
using Camledian.Photobooth.Core.Models;

namespace Camledian.Photobooth.Storage.Repositories;

/// <summary>
/// Persists <see cref="AppSettings"/> as one JSON row per top-level section, so the admin screen can
/// save a single section (e.g. just ChromaKey) without touching the rest. Missing rows fall back to
/// the section's own defaults, which is what makes a brand-new database boot with sane settings.
/// </summary>
public class SettingsRepository(SqliteConnectionFactory connectionFactory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        var settings = new AppSettings();
        using var connection = connectionFactory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Key, Value FROM Settings";
        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var key = reader.GetString(0);
            var json = reader.GetString(1);
            ApplySection(settings, key, json);
        }

        return settings;
    }

    public async Task SaveSectionAsync(string sectionKey, object sectionValue, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(sectionValue, sectionValue.GetType(), JsonOptions);
        using var connection = connectionFactory.Create();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Settings (Key, Value, UpdatedAtUtc) VALUES ($key, $value, $now)
            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value, UpdatedAtUtc = excluded.UpdatedAtUtc
            """;
        command.Parameters.AddWithValue("$key", sectionKey);
        command.Parameters.AddWithValue("$value", json);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task SaveAllAsync(AppSettings settings, CancellationToken cancellationToken = default) =>
        Task.WhenAll(
            SaveSectionAsync(nameof(AppSettings.Camera), settings.Camera, cancellationToken),
            SaveSectionAsync(nameof(AppSettings.ChromaKey), settings.ChromaKey, cancellationToken),
            SaveSectionAsync(nameof(AppSettings.Ai), settings.Ai, cancellationToken),
            SaveSectionAsync(nameof(AppSettings.Print), settings.Print, cancellationToken),
            SaveSectionAsync(nameof(AppSettings.Cloud), settings.Cloud, cancellationToken),
            SaveSectionAsync(nameof(AppSettings.Ui), settings.Ui, cancellationToken),
            SaveSectionAsync(nameof(AppSettings.Storage), settings.Storage, cancellationToken));

    private static void ApplySection(AppSettings settings, string key, string json)
    {
        switch (key)
        {
            case nameof(AppSettings.Camera):
                settings.Camera = JsonSerializer.Deserialize<CameraSettings>(json, JsonOptions) ?? settings.Camera;
                break;
            case nameof(AppSettings.ChromaKey):
                settings.ChromaKey = JsonSerializer.Deserialize<ChromaKeySettings>(json, JsonOptions) ?? settings.ChromaKey;
                break;
            case nameof(AppSettings.Ai):
                settings.Ai = JsonSerializer.Deserialize<AiSettings>(json, JsonOptions) ?? settings.Ai;
                break;
            case nameof(AppSettings.Print):
                settings.Print = JsonSerializer.Deserialize<PrintSettings>(json, JsonOptions) ?? settings.Print;
                break;
            case nameof(AppSettings.Cloud):
                settings.Cloud = JsonSerializer.Deserialize<CloudSettings>(json, JsonOptions) ?? settings.Cloud;
                break;
            case nameof(AppSettings.Ui):
                settings.Ui = JsonSerializer.Deserialize<UiSettings>(json, JsonOptions) ?? settings.Ui;
                break;
            case nameof(AppSettings.Storage):
                settings.Storage = JsonSerializer.Deserialize<StorageSettings>(json, JsonOptions) ?? settings.Storage;
                break;
        }
    }
}
