using Microsoft.Data.Sqlite;
using TimeLens.Api.Dtos;

namespace TimeLens.Api.Services;

/// <summary>One visible tab, with finite durable checkpoints and a bounded observation lease.</summary>
public sealed class BrowserTrackingService(string dbPath, TimeProvider? clock = null)
{
    private readonly object _gate = new();
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private BrowserEventDto? _current;
    private long _id;
    private DateTime _lastSeen;
    private DateTime _lastCheckpoint;
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(65);

    public static bool MatchesForeground(string browser, string app) => browser.ToLowerInvariant() switch
    {
        "firefox" => app.ToLowerInvariant() is "firefox.exe" or "zen.exe" or "floorp.exe" or "waterfox.exe" or "librewolf.exe",
        "edge" => app.Equals("msedge.exe", StringComparison.OrdinalIgnoreCase),
        "opera" => app.Equals("opera.exe", StringComparison.OrdinalIgnoreCase),
        "chrome" => app.ToLowerInvariant() is "chrome.exe" or "brave.exe" or "vivaldi.exe" or "arc.exe" or "thorium.exe",
        _ => false
    };

    private bool Eligible(string browser) => LiveStatusStore.Settings.TrackBrowser &&
        !LiveStatusStore.IsIdle && LiveStatusStore.SystemState == "active" &&
        MatchesForeground(browser, LiveStatusStore.CurrentApp);

    public void Observe(BrowserEventDto evt)
    {
        lock (_gate)
        {
            var now = _clock.GetUtcNow().UtcDateTime;
            // Delayed requests must never be replayed as current activity.
            if (evt.ObservedAt is { } observed && Math.Abs((now - observed.UtcDateTime).TotalSeconds) > 10) return;
            if (evt.TabId <= 0 || !Uri.TryCreate(evt.Url, UriKind.Absolute, out var uri) ||
                uri.Scheme is not ("http" or "https") || !Eligible(evt.Browser)) return;
            evt = evt with { Domain = uri.Host.ToLowerInvariant() };
            using var conn = Open();
            using var tx = conn.BeginTransaction();
            if (_current is not null && now >= _lastCheckpoint && now - _lastCheckpoint <= TimeSpan.FromSeconds(30) && now - _lastSeen <= Lease)
            {
                SaveEnd(conn, now);
                if (_current.TabId == evt.TabId && _current.Browser == evt.Browser &&
                    _current.Url == evt.Url)
                {
                    using var title = conn.CreateCommand();
                    title.CommandText = "UPDATE browser_events SET title=$title WHERE id=$id";
                    title.Parameters.AddWithValue("$title", evt.Title ?? "");
                    title.Parameters.AddWithValue("$id", _id);
                    title.ExecuteNonQuery();
                    tx.Commit();
                    _lastSeen = _lastCheckpoint = now;
                    _current = evt;
                    return;
                }
            }
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO browser_events(domain,url,title,browser,tab_id,start_time,end_time,local_date)
                VALUES($domain,$url,$title,$browser,$tab,$now,$now,$date);
                SELECT last_insert_rowid();
                """;
            cmd.Parameters.AddWithValue("$domain", evt.Domain);
            cmd.Parameters.AddWithValue("$url", evt.Url);
            cmd.Parameters.AddWithValue("$title", evt.Title ?? "");
            cmd.Parameters.AddWithValue("$browser", evt.Browser);
            cmd.Parameters.AddWithValue("$tab", evt.TabId);
            cmd.Parameters.AddWithValue("$now", now.ToString("o"));
            cmd.Parameters.AddWithValue("$date", now.ToLocalTime().ToString("yyyy-MM-dd"));
            var id = (long)cmd.ExecuteScalar()!;
            tx.Commit();
            _id = id;
            _current = evt;
            _lastSeen = _lastCheckpoint = now;
        }
    }

    public void Tick()
    {
        lock (_gate)
        {
            if (_current is null) return;
            var now = _clock.GetUtcNow().UtcDateTime;
            // Missing desktop samples represent an unknown/suspended interval.
            if (now < _lastCheckpoint || now - _lastCheckpoint > TimeSpan.FromSeconds(30) || now - _lastSeen > Lease)
            { _current = null; return; }
            using var conn = Open();
            SaveEnd(conn, now);
            _lastCheckpoint = now;
            if (!Eligible(_current.Browser)) _current = null;
        }
    }

    public void Leave(string browser, int tabId)
    {
        lock (_gate)
        {
            if (_current is null || _current.Browser != browser || (tabId > 0 && _current.TabId != tabId)) return;
            Tick();
            _current = null;
        }
    }

    public bool RecordInput(BrowserInputDto input)
    {
        if (!LiveStatusStore.Settings.TrackBrowser || !LiveStatusStore.Settings.TrackInput) return false;
        var now = _clock.GetUtcNow();
        if (!Guid.TryParse(input.BatchId, out _) || input.Keystrokes is < 0 or > 1000 || input.Clicks is < 0 or > 1000 ||
            input.ObservedAt > now.AddSeconds(5) || input.ObservedAt < now.AddMinutes(-2) ||
            !Uri.TryCreate(input.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            input.Url.Length > 16384 || input.Title is null || input.Title.Length > 4096 ||
            input.Browser is not ("chrome" or "firefox" or "edge" or "opera")) return false;
        lock (_gate)
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            // Retries use the original batch ID, so an ambiguous network failure cannot double counts.
            cmd.CommandText = """
                INSERT OR IGNORE INTO browser_input_batches(batch_id,domain,url,title,browser,timestamp,keystrokes,clicks)
                VALUES($id,$domain,$url,$title,$browser,$time,$keys,$clicks)
                """;
            cmd.Parameters.AddWithValue("$id", input.BatchId);
            cmd.Parameters.AddWithValue("$domain", uri.Host.ToLowerInvariant());
            cmd.Parameters.AddWithValue("$url", input.Url);
            cmd.Parameters.AddWithValue("$title", input.Title);
            cmd.Parameters.AddWithValue("$browser", input.Browser);
            cmd.Parameters.AddWithValue("$time", input.ObservedAt.UtcDateTime.ToString("o"));
            cmd.Parameters.AddWithValue("$keys", input.Keystrokes);
            cmd.Parameters.AddWithValue("$clicks", input.Clicks);
            cmd.ExecuteNonQuery();
            return true;
        }
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        return conn;
    }

    private void SaveEnd(SqliteConnection conn, DateTime now)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE browser_events SET end_time=$now WHERE id=$id";
        cmd.Parameters.AddWithValue("$now", now.ToString("o"));
        cmd.Parameters.AddWithValue("$id", _id);
        cmd.ExecuteNonQuery();
    }
}
