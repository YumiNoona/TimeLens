using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Data.Sqlite;
using TimeLens.Api.Dtos;


namespace TimeLens.Api.Services;

public sealed class AnalyticsService
{
    // Keep one year available so the dashboard can switch ranges without
    // returning a visually incomplete heatmap.
    private const int HeatmapDays = 365;
    private readonly string _connString;
    private readonly ConcurrentDictionary<string, (DashboardResponse data, DateTime cachedAt)> _cache = new();
    private readonly List<string> _cacheOrder = new();
    private readonly object _cacheOrderLock = new();
    private const int MaxCacheEntries = 7;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    private static readonly TimeSpan CacheTtlToday = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LockPruneAge = TimeSpan.FromDays(2);

    public AnalyticsService(string dbPath)
    {
        _connString = $"Data Source={dbPath}";
    }

    public async Task<DashboardResponse> GetDashboardAsync(DateTime? queryDate = null)
    {
        var localDate = DateTime.SpecifyKind((queryDate ?? DateTime.Now).Date, DateTimeKind.Local);
        var cacheKey = localDate.ToString("yyyy-MM-dd");
        var isToday = localDate == DateTime.Now.Date;
        var isYesterday = localDate == DateTime.Now.Date.AddDays(-1);

        // Only cache today and yesterday — skip cache for older dates entirely
        if (isToday || isYesterday)
        {
            if (_cache.TryGetValue(cacheKey, out var entry) && DateTime.UtcNow - entry.cachedAt < CacheTtlToday)
                return entry.data;
        }

        var sem = _locks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync();
        try
        {
            if ((isToday || isYesterday) &&
                _cache.TryGetValue(cacheKey, out var entry) && DateTime.UtcNow - entry.cachedAt < CacheTtlToday)
                return entry.data;

            using var conn = new SqliteConnection(_connString);
            await conn.OpenAsync();

            var today = TimeZoneInfo.ConvertTimeToUtc(localDate);
            var tomorrow = TimeZoneInfo.ConvertTimeToUtc(localDate.AddDays(1));
            var rangeEnd = isToday ? DateTime.UtcNow : tomorrow;

            var summary = await GetSummaryAsync(
                conn,
                localDate.ToString("yyyy-MM-dd"),
                localDate.AddDays(-1).ToString("yyyy-MM-dd"),
                today,
                tomorrow,
                rangeEnd);
            var timeline = await GetTimelineAsync(conn, localDate.ToString("yyyy-MM-dd"), rangeEnd);
            var topApps = await GetTopAppsAsync(conn, localDate.ToString("yyyy-MM-dd"), today, tomorrow, rangeEnd);
            var heatmap = await GetHeatmapAsync(conn, localDate, rangeEnd);
            var categories = await GetCategoriesAsync(conn, localDate.ToString("yyyy-MM-dd"), today, rangeEnd);
            var live = new LiveStatusDto(
                LiveStatusStore.CurrentApp,
                LiveStatusStore.IdleSeconds / 60,
                LiveStatusStore.IsIdle,
                LiveStatusStore.AudibleTab,
                LiveStatusStore.AudioActive,
                LiveStatusStore.SystemState,
                LiveStatusStore.PendingIdleReturn
            );

            var browserSites = await GetBrowserSummaryAsync(conn, localDate.ToString("yyyy-MM-dd"));
            var audioSessions = await GetAudioSummaryAsync(conn, today, tomorrow);

            var result = new DashboardResponse(summary, timeline, topApps, heatmap, categories, live, browserSites, audioSessions);

            if (isToday || isYesterday)
            {
                _cache[cacheKey] = (result, DateTime.UtcNow);
                lock (_cacheOrderLock)
                {
                    _cacheOrder.Remove(cacheKey);
                    _cacheOrder.Add(cacheKey);
                    while (_cacheOrder.Count > MaxCacheEntries)
                    {
                        var oldest = _cacheOrder[0];
                        _cacheOrder.RemoveAt(0);
                        _cache.TryRemove(oldest, out _);
                    }
                }
            }

            return result;
        }
        finally
        {
            sem.Release();
            PruneLocks();
        }
    }

    private static async Task<SummaryDto> GetSummaryAsync(
        SqliteConnection conn,
        string localDate,
        string yesterdayDate,
        DateTime today,
        DateTime tomorrow,
        DateTime rangeEnd)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                COALESCE(SUM(CASE WHEN session_state = 'active' AND COALESCE(category, '') != 'system' THEN
                    MAX(0, MIN(julianday(COALESCE(end_time, $rangeEnd)), julianday($rangeEnd)) - MAX(julianday(start_time), julianday($today))) * 86400
                ELSE 0 END), 0) AS active_secs,
                0 AS idle_secs
            FROM app_events
            WHERE julianday(start_time) < julianday($rangeEnd) AND julianday(COALESCE(end_time, $rangeEnd)) > julianday($today)
            """;
        cmd.Parameters.AddWithValue("$date", localDate);
        cmd.Parameters.AddWithValue("$rangeEnd", rangeEnd.ToString("o"));
        cmd.Parameters.AddWithValue("$previousEnd", today.ToString("o"));
        cmd.Parameters.AddWithValue("$previousStart", today.ToLocalTime().Date.AddDays(-1).ToUniversalTime().ToString("o"));
        cmd.Parameters.AddWithValue("$today", today.ToString("o"));
        cmd.Parameters.AddWithValue("$tomorrow", tomorrow.ToString("o"));

        int activeSecs = 0, idleSecs = 0;
        using (var r = await cmd.ExecuteReaderAsync())
        {
            if (await r.ReadAsync())
            {
                activeSecs = Convert.ToInt32(r["active_secs"]);
                idleSecs = Convert.ToInt32(r["idle_secs"]);
            }
        }

        // Idle belongs to the machine, not to whichever window was foreground when
        // input stopped. Use dedicated spans so Explorer/Figma/browser rows cannot
        // inflate both their own time and the idle total.
        cmd.CommandText = """
            SELECT COALESCE(SUM(MAX(0,
                (MIN(julianday(COALESCE(end_time, $rangeEnd)), julianday($rangeEnd)) -
                 MAX(julianday(start_time), julianday($today))) * 86400
            )), 0)
            FROM idle_spans
            WHERE julianday(start_time) < julianday($rangeEnd) AND julianday(COALESCE(end_time, $rangeEnd)) > julianday($today)
            """;
        idleSecs = Convert.ToInt32(await cmd.ExecuteScalarAsync());

        // Sum active time in productive categories for focus score
        cmd.CommandText = """
            SELECT COALESCE(SUM(
                MAX(0, MIN(julianday(COALESCE(end_time, $rangeEnd)), julianday($rangeEnd)) - MAX(julianday(start_time), julianday($today))) * 86400
            ), 0) FROM app_events
            WHERE julianday(start_time) < julianday($rangeEnd) AND julianday(COALESCE(end_time, $rangeEnd)) > julianday($today)
              AND session_state = 'active'
              AND category IN ('development', 'work', 'documents', 'communication', 'design')
            """;
        var productiveSecs = Convert.ToInt32(await cmd.ExecuteScalarAsync());

        // Sum "other" time — unclassified, treated as neutral in focus score
        cmd.CommandText = """
            SELECT COALESCE(SUM(
                MAX(0, MIN(julianday(COALESCE(end_time, $rangeEnd)), julianday($rangeEnd)) - MAX(julianday(start_time), julianday($today))) * 86400
            ), 0) FROM app_events
            WHERE julianday(start_time) < julianday($rangeEnd) AND julianday(COALESCE(end_time, $rangeEnd)) > julianday($today)
              AND session_state = 'active'
              AND (category = 'other' OR category IS NULL)
            """;
        var otherSecs = Convert.ToInt32(await cmd.ExecuteScalarAsync());

        cmd.CommandText = """
            SELECT COUNT(*) FROM app_events
            WHERE julianday(start_time) < julianday($previousEnd) AND julianday(COALESCE(end_time, $previousEnd)) > julianday($previousStart) AND session_state = 'active'
              AND COALESCE(category, '') != 'system'
            """;
        cmd.Parameters.AddWithValue("$yday", yesterdayDate);
        var hadYesterdayData = Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;

        if (hadYesterdayData)
        {
            cmd.CommandText = """
                SELECT COALESCE(SUM(
                    MAX(0, MIN(julianday(COALESCE(end_time, $previousEnd)), julianday($previousEnd)) - MAX(julianday(start_time), julianday($previousStart))) * 86400
                ), 0) FROM app_events
                WHERE julianday(start_time) < julianday($previousEnd) AND julianday(COALESCE(end_time, $previousEnd)) > julianday($previousStart) AND session_state = 'active'
                  AND COALESCE(category, '') != 'system'
                """;
        }
        var yesterdaySecs = hadYesterdayData ? Convert.ToInt32(await cmd.ExecuteScalarAsync()) : -1;

        cmd.CommandText = """
            SELECT category, COALESCE(SUM(
                MAX(0, MIN(julianday(COALESCE(end_time, $rangeEnd)), julianday($rangeEnd)) - MAX(julianday(start_time), julianday($today))) * 86400
            ), 0) AS secs FROM app_events
            WHERE julianday(start_time) < julianday($rangeEnd) AND julianday(COALESCE(end_time, $rangeEnd)) > julianday($today) AND session_state = 'active' AND category != 'system'
            GROUP BY category ORDER BY secs DESC LIMIT 1
            """;
        string topCat = "—";
        int topCatSecs = 0;
        using (var r = await cmd.ExecuteReaderAsync())
        {
            if (await r.ReadAsync())
            {
                topCat = r.IsDBNull(0) ? "other" : r.GetString(0);
                topCatSecs = Convert.ToInt32(r["secs"]);
            }
        }

        // "other" is unclassified and treated as neutral — excluded from denominator
        // so it doesn't penalize the score. Edge: if everything is "other", score 50% (neutral).
        var scoredSecs = activeSecs - otherSecs;
        var focusScore = activeSecs > 0
            ? scoredSecs <= 0
                ? 50
                : (int)Math.Round((double)productiveSecs / scoredSecs * 100)
            : 0;

        // Input totals
        int totalKeys = 0, totalClicks = 0;
        try
        {
            cmd.CommandText = """
                SELECT
                    COALESCE(SUM(keystroke_count), 0) AS total_keys,
                    COALESCE(SUM(click_count), 0) AS total_clicks
                FROM input_activity
                WHERE timestamp >= $today AND timestamp < $tomorrow
                """;
            using (var r2 = await cmd.ExecuteReaderAsync())
            {
                if (await r2.ReadAsync())
                {
                    totalKeys = Convert.ToInt32(r2["total_keys"]);
                    totalClicks = Convert.ToInt32(r2["total_clicks"]);
                }
            }
        }
        catch (Exception ex)
        {
            System.IO.File.AppendAllText(
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "TimeLens", "query_error.log"),
                $"{DateTime.UtcNow:o} input_totals: {ex}{Environment.NewLine}");
        }

        return new SummaryDto(
            FormatDuration(activeSecs), activeSecs,
            FormatDuration(idleSecs), idleSecs,
            focusScore,
            topCat, FormatDuration(topCatSecs),
            yesterdaySecs >= 0 && activeSecs >= 30 * 60 ? (activeSecs - yesterdaySecs) / 60 : null,
            totalKeys,
            totalClicks
        );
    }

    private static async Task<TimelineBlockDto[]> GetTimelineAsync(
        SqliteConnection conn, string localDate, DateTime localEndOfDayUtc)
    {
        var localStartOfDayUtc = DateTime.SpecifyKind(DateTime.ParseExact(localDate, "yyyy-MM-dd", null), DateTimeKind.Local).ToUniversalTime();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT exe_name, window_title, category, start_time, end_time, was_idle, session_state, COALESCE(project,'')
            FROM app_events
            WHERE julianday(start_time) < julianday($end) AND julianday(COALESCE(end_time, $end)) > julianday($start)
              AND COALESCE(category, '') != 'system'
            ORDER BY start_time
            """;
        cmd.Parameters.AddWithValue("$start", localStartOfDayUtc.ToString("o"));
        cmd.Parameters.AddWithValue("$end", localEndOfDayUtc.ToString("o"));

        var blocks = new List<TimelineBlockDto>();
        using var r = await cmd.ExecuteReaderAsync();

        while (await r.ReadAsync())
        {
            var exeName = r.IsDBNull(0) ? "" : r.GetString(0);
            var windowTitle = r.IsDBNull(1) ? null : r.GetString(1);
            var cat = r.IsDBNull(2) ? null : r.GetString(2);
            var start = DateTime.Parse(r.GetString(3), null, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            if (start < localStartOfDayUtc) start = localStartOfDayUtc;
            var endStr = r.IsDBNull(4) ? null : r.GetString(4);
            var isOngoing = endStr is null;
            var endRaw = isOngoing
                ? DateTime.UtcNow
                : DateTime.Parse(endStr!, null, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            var end = endRaw > localEndOfDayUtc ? localEndOfDayUtc : endRaw;
            var sessionState = r.IsDBNull(6) ? (r.GetInt32(5) == 1 ? "idle" : "active") : r.GetString(6);
            var project = r.IsDBNull(7) ? null : r.GetString(7);

            var localStart = TimeZoneInfo.ConvertTimeFromUtc(start, TimeZoneInfo.Local);
            var localEnd = TimeZoneInfo.ConvertTimeFromUtc(end, TimeZoneInfo.Local);

            var startHour = localStart.TimeOfDay.TotalHours;
            var endHour = localEnd.Date > localStart.Date ? 24.0 : localEnd.TimeOfDay.TotalHours;

            if (endHour <= startHour) continue;
            var durationSecs = (int)Math.Round((end - start).TotalSeconds);

            var type = sessionState == "active" ? (cat ?? "other") : sessionState;

            if (!isOngoing && blocks.Count > 0 && blocks[^1].Type == type &&
                string.Equals(blocks[^1].ExeName, exeName, StringComparison.OrdinalIgnoreCase) &&
                Math.Abs(blocks[^1].EndHour - startHour) < 20.0 / 3600.0)
            {
                blocks[^1] = blocks[^1] with { EndHour = endHour, DurationSeconds = blocks[^1].DurationSeconds + durationSecs };
            }
            else
            {
                blocks.Add(new TimelineBlockDto(startHour, endHour, type, exeName, windowTitle, durationSecs, project));
            }
        }

        // Query idle spans so they appear as "idle" blocks in the timeline
        using var idleCmd = conn.CreateCommand();
        idleCmd.CommandText = """
            SELECT MAX(julianday(start_time), julianday($t0)), MIN(julianday(COALESCE(end_time, $eod)), julianday($eod)), COALESCE(idle_reason, 'idle')
            FROM idle_spans
            WHERE julianday(start_time) < julianday($t1) AND julianday(COALESCE(end_time, $eod)) > julianday($t0)
            ORDER BY start_time
            """;
        var localEndOfDayStr = localEndOfDayUtc.ToString("o");
        idleCmd.Parameters.AddWithValue("$t0", localStartOfDayUtc.ToString("o"));
        idleCmd.Parameters.AddWithValue("$t1", localEndOfDayStr);
        idleCmd.Parameters.AddWithValue("$eod", localEndOfDayStr);

        using var ir = await idleCmd.ExecuteReaderAsync();
        while (await ir.ReadAsync())
        {
            var idleStart = DateTime.UnixEpoch.AddDays(ir.GetDouble(0) - 2440587.5);
            var idleEnd = DateTime.UnixEpoch.AddDays(ir.GetDouble(1) - 2440587.5);
            var reason = ir.GetString(2);

            var localStart = TimeZoneInfo.ConvertTimeFromUtc(idleStart, TimeZoneInfo.Local);
            var localEnd = TimeZoneInfo.ConvertTimeFromUtc(idleEnd, TimeZoneInfo.Local);

            var startHour = localStart.TimeOfDay.TotalHours;
            var endHour = localEnd.Date > localStart.Date ? 24.0 : localEnd.TimeOfDay.TotalHours;

            if (endHour <= startHour) continue;
            var durationSecs = (int)Math.Round((idleEnd - idleStart).TotalSeconds);

            blocks.Add(new TimelineBlockDto(startHour, endHour, reason == "away" ? "away" : "idle", reason, null, durationSecs));
        }

        // Sort merged app-event and idle-span blocks by start time
        blocks.Sort((a, b) => a.StartHour.CompareTo(b.StartHour));

        // Merge consecutive same-category blocks separated by <30s gaps.
        // Collapses the Browsing ↔ Development ping-pong from rapid alt-tabbing.
        for (int i = blocks.Count - 1; i >= 1; i--)
        {
            if (blocks[i].Type == blocks[i - 1].Type &&
                blocks[i].ExeName == blocks[i - 1].ExeName &&
                blocks[i].StartHour - blocks[i - 1].EndHour < 30.0 / 3600)
            {
                blocks[i - 1] = blocks[i - 1] with
                {
                    EndHour = blocks[i].EndHour,
                    DurationSeconds = blocks[i - 1].DurationSeconds + blocks[i].DurationSeconds
                };
                blocks.RemoveAt(i);
            }
        }

        return blocks.ToArray();
    }

    private static async Task<TopAppDto[]> GetTopAppsAsync(
        SqliteConnection conn, string localDate, DateTime today, DateTime tomorrow, DateTime rangeEnd)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT ae.exe_name, COALESCE(SUM(
                MAX(0, MIN(julianday(COALESCE(ae.end_time, $now)), julianday($now)) - MAX(julianday(ae.start_time), julianday($t0))) * 86400
            ), 0) AS secs,
            COALESCE(ia.keys, 0) AS keys,
            COALESCE(ia.clicks, 0) AS clicks
            FROM app_events ae
            LEFT JOIN (
                SELECT exe_name,
                       COALESCE(SUM(keystroke_count), 0) AS keys,
                       COALESCE(SUM(click_count), 0) AS clicks
                FROM input_activity
                WHERE timestamp >= $t0 AND timestamp < $t1 AND exe_name IS NOT NULL
                GROUP BY exe_name
            ) ia ON ia.exe_name = ae.exe_name
            WHERE julianday(ae.start_time) < julianday($now) AND julianday(COALESCE(ae.end_time, $now)) > julianday($t0)
              AND ae.session_state = 'active' AND COALESCE(ae.category, '') != 'system'
            GROUP BY ae.exe_name ORDER BY secs DESC LIMIT 8
            """;
        cmd.Parameters.AddWithValue("$date", localDate);
        cmd.Parameters.AddWithValue("$now", rangeEnd.ToString("o"));
        cmd.Parameters.AddWithValue("$t0", today.ToString("o"));
        cmd.Parameters.AddWithValue("$t1", tomorrow.ToString("o"));

        var apps = new List<TopAppDto>();
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var secs = Convert.ToInt32(r["secs"]);
            var keys = Convert.ToInt32(r["keys"]);
            var clicks = Convert.ToInt32(r["clicks"]);
            apps.Add(new TopAppDto(r.GetString(0), secs / 60, keys, clicks));
        }
        return apps.ToArray();
    }

    private static async Task<HeatmapEntryDto[]> GetHeatmapAsync(
        SqliteConnection conn, DateTime localDate, DateTime rangeEnd)
    {
        var startDate = localDate.AddDays(-(HeatmapDays - 1));
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT start_time, COALESCE(end_time, $end) FROM app_events
            WHERE session_state = 'active' AND COALESCE(category, '') != 'system'
              AND julianday(start_time) < julianday($end)
              AND julianday(COALESCE(end_time, $end)) > julianday($start)
            """;
        var rangeStart = startDate.ToUniversalTime();
        cmd.Parameters.AddWithValue("$start", rangeStart.ToString("o"));
        cmd.Parameters.AddWithValue("$end", rangeEnd.ToString("o"));
        var seconds = new Dictionary<string, double>();
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var start = DateTime.Parse(r.GetString(0), null, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            var end = DateTime.Parse(r.GetString(1), null, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            if (start < rangeStart) start = rangeStart;
            if (end > rangeEnd) end = rangeEnd;
            while (start < end)
            {
                var day = start.ToLocalTime().Date;
                var next = day.AddDays(1).ToUniversalTime();
                var stop = end < next ? end : next;
                var key = day.ToString("yyyy-MM-dd");
                seconds[key] = seconds.GetValueOrDefault(key) + (stop - start).TotalSeconds;
                start = stop;
            }
        }
        var map = seconds.ToDictionary(x => x.Key, x => (int)Math.Round(x.Value) / 60);

        var entries = new List<HeatmapEntryDto>();
        for (int i = 0; i < HeatmapDays; i++)
        {
            var date = startDate.AddDays(i).ToString("yyyy-MM-dd");
            entries.Add(new HeatmapEntryDto(date, map.GetValueOrDefault(date, 0)));
        }
        return entries.ToArray();
    }

    private static async Task<CategoryEntryDto[]> GetCategoriesAsync(
        SqliteConnection conn, string localDate, DateTime today, DateTime rangeEnd)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COALESCE(category, 'other') AS cat, COALESCE(SUM(
                MAX(0, MIN(julianday(COALESCE(end_time, $now)), julianday($now)) - MAX(julianday(start_time), julianday($today))) * 86400
            ), 0) AS secs FROM app_events
            WHERE julianday(start_time) < julianday($now) AND julianday(COALESCE(end_time, $now)) > julianday($today)
              AND session_state = 'active' AND COALESCE(category, '') != 'system'
            GROUP BY cat ORDER BY secs DESC
            """;
        cmd.Parameters.AddWithValue("$today", today.ToString("o"));
        cmd.Parameters.AddWithValue("$now", rangeEnd.ToString("o"));

        var cats = new List<CategoryEntryDto>();
        double totalSecs = 0;

        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var secs = Convert.ToInt32(r["secs"]);
            cats.Add(new CategoryEntryDto(r.GetString(0), 0, secs / 60));
            totalSecs += secs;
        }

        for (int i = 0; i < cats.Count; i++)
        {
            var c = cats[i];
            cats[i] = c with { Percentage = totalSecs > 0 ? Math.Round(c.Minutes * 60 / totalSecs * 100) : 0 };
        }

        return cats.ToArray();
    }

    private static async Task<InputSummaryDto[]> GetInputSummaryAsync(
        SqliteConnection conn, DateTime today, DateTime tomorrow)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT exe_name,
                   COALESCE(SUM(keystroke_count), 0) AS keys,
                   COALESCE(SUM(click_count), 0) AS clicks
            FROM input_activity
            WHERE timestamp >= $today AND timestamp < $tomorrow AND exe_name IS NOT NULL
            GROUP BY exe_name ORDER BY keys DESC
            """;
        cmd.Parameters.AddWithValue("$today", today.ToString("o"));
        cmd.Parameters.AddWithValue("$tomorrow", tomorrow.ToString("o"));

        var list = new List<InputSummaryDto>();
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new InputSummaryDto(
                r.IsDBNull(0) ? "" : r.GetString(0),
                Convert.ToInt32(r["keys"]),
                Convert.ToInt32(r["clicks"])));
        }
        return list.ToArray();
    }

    private static async Task<BrowserEntryDto[]> GetBrowserSummaryAsync(
        SqliteConnection conn, string localDate)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT domain,
                   COUNT(*) AS visits,
                   MAX(start_time) AS last_visit
            FROM browser_events
            WHERE local_date = $date
            GROUP BY domain ORDER BY visits DESC LIMIT 20
            """;
        cmd.Parameters.AddWithValue("$date", localDate);

        var list = new List<BrowserEntryDto>();
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new BrowserEntryDto(
                r.IsDBNull(0) ? "" : r.GetString(0),
                Convert.ToInt32(r["visits"]),
                r.IsDBNull(2) ? "" : r.GetString(2)));
        }
        return list.ToArray();
    }

    private static async Task<AudioSessionDto[]> GetAudioSummaryAsync(
        SqliteConnection conn, DateTime today, DateTime tomorrow)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT exe_name,
                   COUNT(*) AS sessions,
                   MIN(timestamp) AS first_seen
            FROM audio_activity
            WHERE is_playing = 1 AND timestamp >= $today AND timestamp < $tomorrow
            GROUP BY exe_name ORDER BY sessions DESC
            """;
        cmd.Parameters.AddWithValue("$today", today.ToString("o"));
        cmd.Parameters.AddWithValue("$tomorrow", tomorrow.ToString("o"));

        var list = new List<AudioSessionDto>();
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new AudioSessionDto(
                r.IsDBNull(0) ? "" : r.GetString(0),
                Convert.ToInt32(r["sessions"]),
                r.IsDBNull(2) ? "" : r.GetString(2)));
        }
        return list.ToArray();
    }

    private void PruneLocks()
    {
        var cutoff = DateTime.UtcNow.Subtract(LockPruneAge).ToString("yyyy-MM-dd");
        foreach (var key in _locks.Keys)
        {
            if (string.CompareOrdinal(key, cutoff) < 0)
                _locks.TryRemove(key, out _);
        }
    }

    private static string FormatDuration(int totalSecs)
    {
        var h = totalSecs / 3600;
        var m = (totalSecs % 3600) / 60;
        return $"{h}h {m:D2}m";
    }
}
