using Microsoft.Data.Sqlite;

namespace TimeLens.TrayApp.Services;

public sealed class EventWriter
{
    private readonly SqliteConnection _conn;
    private readonly object _lock = new();
    private long? _openAppEventId;

    public EventWriter(string dbPath)
    {
        _conn = new SqliteConnection($"Data Source={dbPath}");
        _conn.Open();
        using var wal = _conn.CreateCommand();
        wal.CommandText = "PRAGMA journal_mode=WAL;";
        wal.ExecuteNonQuery();
    }

    public void OpenAppEvent(string exeName, string windowTitle, int pid, bool wasIdle, string? category)
    {
        lock (_lock)
        {
            if (_openAppEventId is long openId)
            {
                using var close = _conn.CreateCommand();
                close.CommandText = "UPDATE app_events SET end_time = $now WHERE id = $id";
                close.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
                close.Parameters.AddWithValue("$id", openId);
                close.ExecuteNonQuery();
            }

            using var insert = _conn.CreateCommand();
            insert.CommandText = """
                INSERT INTO app_events (exe_name, window_title, pid, category, start_time, was_idle)
                VALUES ($exe, $title, $pid, $cat, $start, $idle);
                SELECT last_insert_rowid();
                """;
            insert.Parameters.AddWithValue("$exe", exeName);
            insert.Parameters.AddWithValue("$title", windowTitle);
            insert.Parameters.AddWithValue("$pid", pid);
            insert.Parameters.AddWithValue("$cat", category ?? (object)DBNull.Value);
            insert.Parameters.AddWithValue("$start", DateTime.UtcNow.ToString("o"));
            insert.Parameters.AddWithValue("$idle", wasIdle ? 1 : 0);

            _openAppEventId = (long)insert.ExecuteScalar()!;
        }
    }

    public void InsertInputActivity(int keystrokes, int clicks, int? pid, string? exeName)
    {
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO input_activity (timestamp, keystroke_count, click_count, pid, exe_name)
                VALUES ($ts, $keys, $clicks, $pid, $exe)
                """;
            cmd.Parameters.AddWithValue("$ts", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("$keys", keystrokes);
            cmd.Parameters.AddWithValue("$clicks", clicks);
            cmd.Parameters.AddWithValue("$pid", pid ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$exe", exeName ?? (object)DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    public void InsertAudioActivity(int? pid, string? exeName, bool isPlaying)
    {
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO audio_activity (timestamp, pid, exe_name, is_playing)
                VALUES ($ts, $pid, $exe, $playing)
                """;
            cmd.Parameters.AddWithValue("$ts", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("$pid", pid ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$exe", exeName ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$playing", isPlaying ? 1 : 0);
            cmd.ExecuteNonQuery();
        }
    }

    public void InsertSessionEvent(string eventType)
    {
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO session_events (event_type, timestamp)
                VALUES ($type, $ts)
                """;
            cmd.Parameters.AddWithValue("$type", eventType);
            cmd.Parameters.AddWithValue("$ts", DateTime.UtcNow.ToString("o"));
            cmd.ExecuteNonQuery();
        }
    }
}
