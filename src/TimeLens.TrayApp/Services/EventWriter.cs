using Microsoft.Data.Sqlite;

namespace TimeLens.TrayApp.Services;

public sealed class EventWriter
{
    private readonly WriterQueue _queue;
    private long? _openAppEventId;

    public EventWriter(string dbPath)
    {
        _queue = new WriterQueue(dbPath);
    }

    public void OpenAppEvent(string exeName, string windowTitle, int pid, string sessionState, string? category)
    {
        if (_openAppEventId is long openId)
        {
            _queue.ExecuteSync(conn =>
            {
                using var close = conn.CreateCommand();
                close.CommandText = "UPDATE app_events SET end_time = $now WHERE id = $id";
                close.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
                close.Parameters.AddWithValue("$id", openId);
                close.ExecuteNonQuery();
            });
        }

        _openAppEventId = _queue.ExecuteSyncWithRowId(conn =>
        {
            using var insert = conn.CreateCommand();
            insert.CommandText = """
                INSERT INTO app_events (exe_name, window_title, pid, category, start_time, session_state, was_idle, local_date)
                VALUES ($exe, $title, $pid, $cat, $start, $state, CASE WHEN $state = 'active' THEN 0 ELSE 1 END, $localDate);
                """;
            insert.Parameters.AddWithValue("$exe", exeName);
            insert.Parameters.AddWithValue("$title", windowTitle);
            insert.Parameters.AddWithValue("$pid", pid);
            insert.Parameters.AddWithValue("$cat", category ?? (object)DBNull.Value);
            insert.Parameters.AddWithValue("$start", DateTime.UtcNow.ToString("o"));
            insert.Parameters.AddWithValue("$state", sessionState);
            insert.Parameters.AddWithValue("$localDate", DateTime.Now.ToString("yyyy-MM-dd"));
            insert.ExecuteNonQuery();
        });
    }

    public void InsertInputActivity(int keystrokes, int clicks, int? pid, string? exeName)
    {
        var ts = DateTime.UtcNow.ToString("o");
        _queue.Enqueue(cmd =>
        {
            cmd.CommandText = """
                INSERT INTO input_activity (timestamp, keystroke_count, click_count, pid, exe_name)
                VALUES ($ts, $keys, $clicks, $pid, $exe)
                """;
            cmd.Parameters.AddWithValue("$ts", ts);
            cmd.Parameters.AddWithValue("$keys", keystrokes);
            cmd.Parameters.AddWithValue("$clicks", clicks);
            cmd.Parameters.AddWithValue("$pid", pid ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$exe", exeName ?? (object)DBNull.Value);
        });
    }

    public void InsertAudioActivity(int? pid, string? exeName, bool isPlaying)
    {
        var ts = DateTime.UtcNow.ToString("o");
        _queue.Enqueue(cmd =>
        {
            cmd.CommandText = """
                INSERT INTO audio_activity (timestamp, pid, exe_name, is_playing)
                VALUES ($ts, $pid, $exe, $playing)
                """;
            cmd.Parameters.AddWithValue("$ts", ts);
            cmd.Parameters.AddWithValue("$pid", pid ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$exe", exeName ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$playing", isPlaying ? 1 : 0);
        });
    }

    public void InsertSessionEvent(string eventType)
    {
        var ts = DateTime.UtcNow.ToString("o");
        _queue.Enqueue(cmd =>
        {
            cmd.CommandText = """
                INSERT INTO session_events (event_type, timestamp)
                VALUES ($type, $ts)
                """;
            cmd.Parameters.AddWithValue("$type", eventType);
            cmd.Parameters.AddWithValue("$ts", ts);
        });
    }
}
