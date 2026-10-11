using Microsoft.Data.Sqlite;

namespace TimeLens.TrayApp.Services;

public sealed class EventWriter : IDisposable
{
    private readonly WriterQueue _queue;
    private long? _lastOpenEventId;
    private readonly object _openEventLock = new();

    public EventWriter(string dbPath, TimeProvider? clock = null)
    {
        _queue = new WriterQueue(dbPath);
        _clock = clock ?? TimeProvider.System;
    }

    private readonly TimeProvider _clock;
    private (string Exe, string Title, int Pid, string State, string? Category, string? Project)? _identity;
    private DateTime _lastObservation;
    private DateTime _lastPersisted;
    private static readonly TimeSpan CheckpointInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaximumGap = TimeSpan.FromSeconds(30);

    public void OpenAppEvent(string exeName, string windowTitle, int pid, string sessionState, string? category, string? project = null)
    {
        lock (_openEventLock)
        {
            if (TimeLens.Api.PrivacyPolicy.IgnoreApp(exeName)) { CloseCurrentAppEvent(); return; }
            windowTitle = TimeLens.Api.PrivacyPolicy.Title(windowTitle, exeName);
            if (windowTitle.Length == 0) project = null;
            var now = _clock.GetUtcNow().UtcDateTime;
            var identity = (exeName, windowTitle, pid, sessionState, category, project);
            var continuous = now >= _lastObservation && now - _lastObservation <= MaximumGap;
            if (_lastOpenEventId is long current && _identity == identity && continuous)
            {
                if (now - _lastPersisted >= CheckpointInterval) { SaveEnd(current, now); _lastPersisted = now; }
                _lastObservation = now;
                return;
            }
            // Never bridge an unobserved suspend, stalled timer, or clock jump.
            if (_lastOpenEventId is long previous && continuous) SaveEnd(previous, now);
            var newId = _queue.ExecuteSyncWithRowId(conn =>
            {
                using var insert = conn.CreateCommand();
                insert.CommandText = """
                    INSERT INTO app_events (exe_name, window_title, pid, category, start_time, end_time, session_state, was_idle, local_date, project)
                    VALUES ($exe, $title, $pid, $cat, $now, $now, $state, CASE WHEN $state = 'active' THEN 0 ELSE 1 END, $date, $project)
                    """;
                insert.Parameters.AddWithValue("$exe", exeName);
                insert.Parameters.AddWithValue("$title", windowTitle);
                insert.Parameters.AddWithValue("$pid", pid);
                insert.Parameters.AddWithValue("$cat", category ?? (object)DBNull.Value);
                insert.Parameters.AddWithValue("$now", now.ToString("o"));
                insert.Parameters.AddWithValue("$state", sessionState);
                insert.Parameters.AddWithValue("$date", now.ToLocalTime().ToString("yyyy-MM-dd"));
                insert.Parameters.AddWithValue("$project", project ?? (object)DBNull.Value);
                insert.ExecuteNonQuery();
            });
            _lastOpenEventId = newId;
            _identity = identity;
            _lastObservation = now;
            _lastPersisted = now;
        }
    }

    private void SaveEnd(long id, DateTime now) => _queue.ExecuteSync(conn =>
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE app_events SET end_time = $now WHERE id = $id";
        cmd.Parameters.AddWithValue("$now", now.ToString("o"));
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    });

    public void CloseCurrentAppEvent()
    {
        lock (_openEventLock)
        {
            var now = _clock.GetUtcNow().UtcDateTime;
            if (_lastOpenEventId is long id && now >= _lastObservation && now - _lastObservation <= MaximumGap)
                SaveEnd(id, now);
            _lastOpenEventId = null;
            _identity = null;
        }
    }

    public void ClearAllActivity()
    {
        CloseCurrentAppEvent();
        EndIdleSpan();
        _queue.ExecuteSync(conn =>
        {
            using var tx = conn.BeginTransaction();
            foreach (var table in new[] { "browser_input_batches", "browser_events", "web_media_events", "app_events", "session_events", "input_activity", "audio_activity", "block_log", "offline_entries", "idle_spans" })
            {
                using var delete = conn.CreateCommand();
                delete.Transaction = tx;
                delete.CommandText = $"DELETE FROM {table}";
                delete.ExecuteNonQuery();
            }
            tx.Commit();
        });
    }

    public void Dispose()
    {
        CloseCurrentAppEvent();
        EndIdleSpan();
        _queue.Dispose();
    }

    public void InsertInputActivity(int keystrokes, int clicks, int? pid, string? exeName, DateTime? observedAt = null)
    {
        if (TimeLens.Api.PrivacyPolicy.IgnoreApp(exeName ?? "") ||
            !TimeLens.Api.PrivacyPolicy.AcceptObservation(new DateTimeOffset((observedAt ?? _clock.GetUtcNow().UtcDateTime).ToUniversalTime()))) return;
        var ts = (observedAt ?? _clock.GetUtcNow().UtcDateTime).ToUniversalTime().ToString("o");
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
        if (TimeLens.Api.PrivacyPolicy.IgnoreApp(exeName ?? "")) return;
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
        if (TimeLens.Api.PrivacyPolicy.IsPaused) return;
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
        if (TimeLens.Api.PrivacyPolicy.IgnoreApp(exeName) ||
            (!exeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && TimeLens.Api.PrivacyPolicy.IgnoreDomain(exeName))) return;
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
    private DateTime _lastIdleObservation;
    private DateTime _lastIdlePersisted;
    private string? _idleReason;
    private readonly object _idleSpanLock = new();

    public bool StartIdleSpan(string exeName, string reason)
    {
        lock (_idleSpanLock)
        {
            if (TimeLens.Api.PrivacyPolicy.IsPaused) { EndIdleSpan(); return false; }
            if (TimeLens.Api.PrivacyPolicy.IgnoreApp(exeName)) exeName = "";
            var now = _clock.GetUtcNow().UtcDateTime;
            if (_idleSpanId is not null)
            {
                var continuous = now >= _lastIdleObservation && now - _lastIdleObservation <= MaximumGap;
                if (continuous && reason == _idleReason)
                {
                    if (now - _lastIdlePersisted >= CheckpointInterval) { SaveIdleEnd(now); _lastIdlePersisted = now; }
                    _lastIdleObservation = now;
                    return false;
                }
                EndIdleSpan();
            }
            _idleSpanId = _queue.ExecuteSyncWithRowId(conn =>
            {
                using var insert = conn.CreateCommand();
                insert.CommandText = """
                    INSERT INTO idle_spans (start_time, end_time, exe_at_start, idle_reason)
                    VALUES ($start, $start, $exe, $reason)
                    """;
                insert.Parameters.AddWithValue("$start", now.ToString("o"));
                insert.Parameters.AddWithValue("$exe", exeName);
                insert.Parameters.AddWithValue("$reason", reason);
                insert.ExecuteNonQuery();
            });
            _lastIdleObservation = now;
            _lastIdlePersisted = now;
            _idleReason = reason;
            return true;
        }
    }

    private void SaveIdleEnd(DateTime now) => _queue.ExecuteSync(conn =>
    {
        using var update = conn.CreateCommand();
        update.CommandText = "UPDATE idle_spans SET end_time = $now WHERE id = $id";
        update.Parameters.AddWithValue("$now", now.ToString("o"));
        update.Parameters.AddWithValue("$id", _idleSpanId!.Value);
        update.ExecuteNonQuery();
    });

    public bool EndIdleSpan()
    {
        lock (_idleSpanLock)
        {
            if (_idleSpanId is null) return false;
            var now = _clock.GetUtcNow().UtcDateTime;
            if (now >= _lastIdleObservation && now - _lastIdleObservation <= MaximumGap) SaveIdleEnd(now);
            _queue.ExecuteSync(conn =>
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE idle_spans SET reviewed=0 WHERE id=$id AND (julianday(end_time)-julianday(start_time))*86400 >= 60";
                cmd.Parameters.AddWithValue("$id", _idleSpanId.Value);
                cmd.ExecuteNonQuery();
            });
            _idleSpanId = null;
            return true;
        }
    }
}
