using Microsoft.Data.Sqlite;
using TimeLens.Api;

namespace TimeLens.TrayApp.Services;

public sealed class SettingsService
{
    private readonly string _connectionString;

    public SettingsService(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
    }

    public AppSettings Load()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT key, value FROM settings";
        using var reader = cmd.ExecuteReader();
        var dict = new Dictionary<string, string>();
        while (reader.Read())
            dict[reader.GetString(0)] = reader.GetString(1);

        return new AppSettings
        {
            TrackAudio = dict.GetValueOrDefault("track_audio", "true") == "true",
            TrackBrowser = dict.GetValueOrDefault("track_browser", "true") == "true",
            TrackInput = dict.GetValueOrDefault("track_input", "true") == "true",
            IdleThresholdSeconds = int.TryParse(dict.GetValueOrDefault("idle_threshold_seconds", "180"), out var t) ? t : 180,
            Theme = dict.GetValueOrDefault("theme", "moss").Trim('"'),
        };
    }

    public void Save(string key, string value)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO settings (key, value) VALUES ($key, $value)";
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$value", value);
        cmd.ExecuteNonQuery();
    }
}
