using Microsoft.Data.Sqlite;
using TimeLens.Api.Dtos;

namespace TimeLens.Api.Services;

/// <summary>
/// Persists bounded web-media intervals independently from foreground activity.
/// Playback is intentionally an overlay and is never added to primary active time.
/// </summary>
public sealed class MediaTrackingService(string dbPath, TimeProvider? clock = null)
{
    private sealed record OpenMedia(long Id, MediaStateDto State, DateTime LastSeenUtc);

    private readonly object _gate = new();
    private readonly Dictionary<string, OpenMedia> _open = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(45);

    public bool Observe(MediaStateDto state)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        if (!Validate(state, now, out var uri)) return false;
        var key = $"{state.Browser}:{state.TabId}:{state.MediaId}";

        lock (_gate)
        {
            if (!state.Playing || !LiveStatusStore.Settings.TrackAudio || !LiveStatusStore.Settings.TrackBrowser)
            {
                Close(key, now);
                return true;
            }

            var normalized = state with
            {
                Url = PrivacyUrl(uri),
                Title = LiveStatusStore.Settings.BrowserStoreTitles ? state.Title : string.Empty,
                Kind = state.Kind is "audio" or "video" ? state.Kind : "unknown",
                Visibility = state.Visibility is "foreground" or "background" or "pip" ? state.Visibility : "background",
                Confidence = state.Confidence is "media-element" or "audible-fallback" ? state.Confidence : "media-element"
            };

            if (_open.TryGetValue(key, out var current))
            {
                var continuous = now >= current.LastSeenUtc && now - current.LastSeenUtc <= Lease;
                var sameIdentity = continuous && SameIdentity(current.State, normalized);
                if (sameIdentity)
                {
                    SaveEnd(current.Id, now);
                    _open[key] = current with { State = normalized, LastSeenUtc = now };
                    return true;
                }
                if (continuous) SaveEnd(current.Id, now);
                _open.Remove(key);
            }

            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO web_media_events
                    (media_key,domain,url,title,browser,tab_id,media_kind,start_time,end_time,
                     is_pip,is_audible,is_muted,visibility,confidence,local_date)
                VALUES
                    ($key,$domain,$url,$title,$browser,$tab,$kind,$now,$now,
                     $pip,$audible,$muted,$visibility,$confidence,$date);
                SELECT last_insert_rowid();
                """;
            cmd.Parameters.AddWithValue("$key", key);
            cmd.Parameters.AddWithValue("$domain", uri.Host.ToLowerInvariant());
            cmd.Parameters.AddWithValue("$url", normalized.Url);
            cmd.Parameters.AddWithValue("$title", normalized.Title);
            cmd.Parameters.AddWithValue("$browser", normalized.Browser);
            cmd.Parameters.AddWithValue("$tab", normalized.TabId);
            cmd.Parameters.AddWithValue("$kind", normalized.Kind);
            cmd.Parameters.AddWithValue("$now", now.ToString("o"));
            cmd.Parameters.AddWithValue("$pip", normalized.PictureInPicture ? 1 : 0);
            cmd.Parameters.AddWithValue("$audible", normalized.Audible ? 1 : 0);
            cmd.Parameters.AddWithValue("$muted", normalized.Muted ? 1 : 0);
            cmd.Parameters.AddWithValue("$visibility", normalized.Visibility);
            cmd.Parameters.AddWithValue("$confidence", normalized.Confidence);
            cmd.Parameters.AddWithValue("$date", now.ToLocalTime().ToString("yyyy-MM-dd"));
            var id = (long)cmd.ExecuteScalar()!;
            _open[key] = new(id, normalized, now);
            return true;
        }
    }

    public void Tick()
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        lock (_gate)
        {
            foreach (var key in _open.Where(pair => now < pair.Value.LastSeenUtc || now - pair.Value.LastSeenUtc > Lease)
                         .Select(pair => pair.Key).ToArray())
                _open.Remove(key);
        }
    }

    public void Reset()
    {
        lock (_gate) _open.Clear();
    }

    private void Close(string key, DateTime now)
    {
        if (!_open.Remove(key, out var current)) return;
        if (now >= current.LastSeenUtc && now - current.LastSeenUtc <= Lease) SaveEnd(current.Id, now);
    }

    private static bool SameIdentity(MediaStateDto left, MediaStateDto right) =>
        left.Url == right.Url && left.Browser == right.Browser && left.TabId == right.TabId &&
        left.Kind == right.Kind && left.PictureInPicture == right.PictureInPicture &&
        left.Audible == right.Audible && left.Muted == right.Muted &&
        left.Visibility == right.Visibility && left.Confidence == right.Confidence;

    private static bool Validate(MediaStateDto state, DateTime now, out Uri uri)
    {
        uri = null!;
        return state.MediaId is { Length: > 0 and <= 160 } && state.TabId > 0 &&
               state.Browser is "firefox" or "chrome" or "edge" or "opera" &&
               (state.Title?.Length ?? 0) <= 4096 && state.Url is { Length: <= 16384 } &&
               Math.Abs((now - state.ObservedAt.UtcDateTime).TotalSeconds) <= 20 &&
               Uri.TryCreate(state.Url, UriKind.Absolute, out uri!) && uri.Scheme is "http" or "https";
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        return conn;
    }

    private void SaveEnd(long id, DateTime now)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE web_media_events SET end_time=$now WHERE id=$id";
        cmd.Parameters.AddWithValue("$now", now.ToString("o"));
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    private static string PrivacyUrl(Uri uri) => LiveStatusStore.Settings.BrowserUrlMode switch
    {
        "domain" => uri.GetLeftPart(UriPartial.Authority) + "/",
        "path" => uri.GetLeftPart(UriPartial.Path),
        _ => uri.AbsoluteUri
    };
}
