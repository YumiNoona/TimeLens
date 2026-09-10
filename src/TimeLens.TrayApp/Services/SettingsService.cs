using Microsoft.Data.Sqlite;
using TimeLens.Api;

namespace TimeLens.TrayApp.Services;

public sealed class SettingsService
{
    private readonly string _connectionString;
    private readonly string _dataDirectory;

    public SettingsService(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
        _dataDirectory = Path.GetDirectoryName(Path.GetFullPath(dbPath))!;
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
            BrowserUrlMode = dict.GetValueOrDefault("browser_url_mode", "full") switch { "domain" => "domain", "path" => "path", _ => "full" },
            BrowserStoreTitles = dict.GetValueOrDefault("browser_store_titles", "true") == "true",
            IdleThresholdSeconds = ReadInt(dict, "idle_threshold_seconds", 180, 15, 3600),
            Theme = ReadChoice(dict, "theme", "default", "default", "terminal", "copper", "arctic", "moss", "crimson", "gold", "ember", "rose", "clay", "sunset"),
            TimelineGrouped = dict.GetValueOrDefault("timeline_grouped", "true") == "true",
            AutoStart = dict.GetValueOrDefault("auto_start", "false") == "true",
            RetentionDays = int.TryParse(dict.GetValueOrDefault("retention_days", "90"), out var rd) && rd is >= 1 and <= 3650 ? rd : 90,
            ShowTitles = dict.GetValueOrDefault("show_titles", "false") == "true",
            BreakReminder = dict.GetValueOrDefault("break_reminder", "false") == "true",
            BreakIntervalMinutes = ReadInt(dict, "break_interval_minutes", 50, 5, 240),
            FocusMode = dict.GetValueOrDefault("focus_mode", "false") == "true",
            FocusBlocklist = dict.GetValueOrDefault("focus_blocklist", "[]"),
            TimeFormat = ReadChoice(dict, "time_format", "12h", "12h", "24h"),
            ShowSeconds = dict.GetValueOrDefault("show_seconds", "false") == "true",
            PollIntervalSeconds = ReadInt(dict, "poll_interval_seconds", 30, 5, 300),
            BlockAction = dict.GetValueOrDefault("block_action", "hide"),
            BlockTitle = BlockNotification.NormalizeTitle(dict.GetValueOrDefault("block_title")),
            BlockMessage = BlockNotification.NormalizeMessage(dict.GetValueOrDefault("block_message")),
            BlockImageVersion = HasBlockMedia(dict.GetValueOrDefault("block_media_type", "")) ? dict.GetValueOrDefault("block_image_version", "") : "",
            BlockMediaType = NormalizeMediaType(dict.GetValueOrDefault("block_media_type", "")),
            BlockNotifyIntervalSeconds = int.TryParse(dict.GetValueOrDefault("block_notify_interval_seconds", "300"), out var notifyInterval) ? Math.Clamp(notifyInterval, 5, 86400) : 300,
            BlockNotifyPosition = NormalizeNotifyPosition(dict.GetValueOrDefault("block_notify_position", "bottom-left")),
            BlockMediaLayout = NormalizeMediaLayout(dict.GetValueOrDefault("block_media_layout", "large")),
            DefaultView = ReadChoice(dict, "default_view", "today", "today", "history", "apps", "browser", "timeline", "block", "rules", "settings"),
            Density = ReadChoice(dict, "density", "comfortable", "comfortable", "compact"),
            MotionEnabled = dict.GetValueOrDefault("motion_enabled", "true") == "true",
            TimelineMinSegmentSeconds = int.Parse(ReadChoice(dict, "timeline_min_segment_seconds", "60", "30", "60", "120", "300")),
            HeatmapDays = int.Parse(ReadChoice(dict, "heatmap_days", "273", "28", "91", "273", "365")),
            BlockProtectionEnabled = dict.GetValueOrDefault("block_protection_enabled", "false") == "true",
            BlockProtectionScope = dict.GetValueOrDefault("block_protection_scope", "strict") == "all" ? "all" : "strict",
            BlockExitProtection = dict.GetValueOrDefault("block_exit_protection", "true") != "false",
        };
    }

    private static int ReadInt(IReadOnlyDictionary<string, string> values, string key, int fallback, int minimum, int maximum) =>
        int.TryParse(values.GetValueOrDefault(key), out var parsed) && parsed >= minimum && parsed <= maximum ? parsed : fallback;

    private static string ReadChoice(IReadOnlyDictionary<string, string> values, string key, string fallback, params string[] allowed)
    {
        var value = values.GetValueOrDefault(key, fallback).Trim('"');
        return allowed.Contains(value, StringComparer.Ordinal) ? value : fallback;
    }

    private string NormalizeMediaType(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) return value;
        return File.Exists(Path.Combine(_dataDirectory, "block-notification.png")) ? "image/png" : "";
    }

    private bool HasBlockMedia(string? mediaType)
    {
        var extension = mediaType switch
        {
            "image/jpeg" => "jpg",
            "image/gif" => "gif",
            "video/mp4" => "mp4",
            "video/webm" => "webm",
            _ => "png"
        };
        return File.Exists(Path.Combine(_dataDirectory, $"block-notification-media.{extension}")) ||
               File.Exists(Path.Combine(_dataDirectory, "block-notification.png"));
    }

    private static string NormalizeNotifyPosition(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "top-left" => "top-left",
        "top-right" => "top-right",
        "bottom-right" or "right" => "bottom-right",
        _ => "bottom-left"
    };

    private static string NormalizeMediaLayout(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "thumbnail" => "thumbnail",
        "banner" => "banner",
        _ => "large"
    };

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
