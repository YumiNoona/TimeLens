using Microsoft.Data.Sqlite;

namespace TimeLens.TrayApp.Services;

public sealed class EventWriter
{
    private readonly WriterQueue _queue;
    private long? _lastOpenEventId;

    public EventWriter(string dbPath)
    {
        _queue = new WriterQueue(dbPath);
    }

    public void OpenAppEvent(string exeName, string windowTitle, int pid, string sessionState, string? category, string? project = null)
    {
        _lastOpenEventId = _queue.ExecuteSyncWithRowId(conn =>
        {
            var now = DateTime.UtcNow.ToString("o");
            var since = DateTime.UtcNow.AddHours(-48).ToString("o");

            if (_lastOpenEventId is not (long prevId))
            {
                // First call after startup — clean up any orphans from a previous crash
                using var closeAll = conn.CreateCommand();
                closeAll.CommandText = "UPDATE app_events SET end_time = $now WHERE end_time IS NULL AND start_time >= $since";
                closeAll.Parameters.AddWithValue("$now", now);
                closeAll.Parameters.AddWithValue("$since", since);
                closeAll.ExecuteNonQuery();
            }
            else
            {
                // Close only the previous row we created — avoids race killing
                // freshly-inserted rows during rapid foreground switches.
                using var closePrev = conn.CreateCommand();
                closePrev.CommandText = "UPDATE app_events SET end_time = $now WHERE id = $id AND end_time IS NULL";
                closePrev.Parameters.AddWithValue("$now", now);
                closePrev.Parameters.AddWithValue("$id", prevId);
                closePrev.ExecuteNonQuery();
            }

            // Insert new row
            using var insert = conn.CreateCommand();
            insert.CommandText = """
                INSERT INTO app_events (exe_name, window_title, pid, category, start_time, session_state, was_idle, local_date, project)
                VALUES ($exe, $title, $pid, $cat, $start, $state, CASE WHEN $state = 'active' THEN 0 ELSE 1 END, $localDate, $project);
                """;
            insert.Parameters.AddWithValue("$exe", exeName);
            insert.Parameters.AddWithValue("$title", windowTitle);
            insert.Parameters.AddWithValue("$pid", pid);
            insert.Parameters.AddWithValue("$cat", category ?? (object)DBNull.Value);
            insert.Parameters.AddWithValue("$start", now);
            insert.Parameters.AddWithValue("$state", sessionState);
            insert.Parameters.AddWithValue("$localDate", DateTime.Now.ToString("yyyy-MM-dd"));
            insert.Parameters.AddWithValue("$project", project ?? (object)DBNull.Value);
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

    public void InsertBlockLog(string exeName, string action)
    {
        var ts = DateTime.UtcNow.ToString("o");
        _queue.Enqueue(cmd =>
        {
            cmd.CommandText = """
                INSERT INTO block_log (blocked_exe, blocked_action, timestamp)
                VALUES ($exe, $action, $ts)
                """;
            cmd.Parameters.AddWithValue("$exe", exeName);
            cmd.Parameters.AddWithValue("$action", action);
            cmd.Parameters.AddWithValue("$ts", ts);
        });
    }

    private long? _idleSpanId;
    private readonly object _idleSpanLock = new();

    public bool StartIdleSpan(string exeName, string reason)
    {
        lock (_idleSpanLock)
        {
            if (_idleSpanId is not null) return false;
            _idleSpanId = _queue.ExecuteSyncWithRowId(conn =>
            {
                using var insert = conn.CreateCommand();
                insert.CommandText = """
                    INSERT INTO idle_spans (start_time, exe_at_start, idle_reason)
                    VALUES ($start, $exe, $reason)
                    """;
                insert.Parameters.AddWithValue("$start", DateTime.UtcNow.ToString("o"));
                insert.Parameters.AddWithValue("$exe", exeName);
                insert.Parameters.AddWithValue("$reason", reason);
                insert.ExecuteNonQuery();
            });
            return true;
        }
    }

    public bool EndIdleSpan()
    {
        lock (_idleSpanLock)
        {
            if (_idleSpanId is not (long id)) return false;
            _idleSpanId = null;
            _queue.ExecuteSync(conn =>
            {
                using var update = conn.CreateCommand();
                update.CommandText = "UPDATE idle_spans SET end_time = $now WHERE id = $id";
                update.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
                update.Parameters.AddWithValue("$id", id);
                update.ExecuteNonQuery();
            });
            return true;
        }
    }
}
