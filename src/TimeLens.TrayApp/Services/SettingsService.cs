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
            Theme = dict.GetValueOrDefault("theme", "default").Trim('"'),
            TimelineGrouped = dict.GetValueOrDefault("timeline_grouped", "true") == "true",
            AutoStart = dict.GetValueOrDefault("auto_start", "false") == "true",
            RetentionDays = int.TryParse(dict.GetValueOrDefault("retention_days", "90"), out var rd) ? rd : 90,
            ShowTitles = dict.GetValueOrDefault("show_titles", "false") == "true",
            BreakReminder = dict.GetValueOrDefault("break_reminder", "false") == "true",
            BreakIntervalMinutes = int.TryParse(dict.GetValueOrDefault("break_interval_minutes", "50"), out var bi) ? bi : 50,
            FocusMode = dict.GetValueOrDefault("focus_mode", "false") == "true",
            FocusBlocklist = dict.GetValueOrDefault("focus_blocklist", "[]"),
            TimeFormat = dict.GetValueOrDefault("time_format", "12h"),
            PollIntervalSeconds = int.TryParse(dict.GetValueOrDefault("poll_interval_seconds", "30"), out var pis) ? pis : 30,
            BlockAction = dict.GetValueOrDefault("block_action", "hide"),
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
