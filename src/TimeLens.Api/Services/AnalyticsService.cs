using Microsoft.Data.Sqlite;
using TimeLens.Api.Dtos;


namespace TimeLens.Api.Services;

public sealed class AnalyticsService
{
    private readonly string _connString;
    private readonly Dictionary<string, (DashboardResponse data, DateTime cachedAt)> _cache = [];
    private readonly object _cacheLock = new();

    private static readonly TimeSpan CacheTtlToday = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CacheTtlHistory = TimeSpan.FromSeconds(300);

    public AnalyticsService(string dbPath)
    {
        _connString = $"Data Source={dbPath}";
    }

    public async Task<DashboardResponse> GetDashboardAsync(DateTime? queryDate = null)
    {
        var localDate = DateTime.SpecifyKind((queryDate ?? DateTime.Now).Date, DateTimeKind.Local);
        var cacheKey = localDate.ToString("yyyy-MM-dd");
        var isToday = localDate == DateTime.Now.Date;
        var ttl = isToday ? CacheTtlToday : CacheTtlHistory;

        lock (_cacheLock)
        {
            if (_cache.TryGetValue(cacheKey, out var entry) && DateTime.UtcNow - entry.cachedAt < ttl)
                return entry.data;
        }

        using var conn = new SqliteConnection(_connString);
        await conn.OpenAsync();

        var today = TimeZoneInfo.ConvertTimeToUtc(localDate);
        var tomorrow = TimeZoneInfo.ConvertTimeToUtc(localDate.AddDays(1));
        var yesterday = TimeZoneInfo.ConvertTimeToUtc(localDate.AddDays(-1));

        var summary = await GetSummaryAsync(conn, today, tomorrow, yesterday);
        var timeline = await GetTimelineAsync(conn, today, tomorrow);
        var topApps = await GetTopAppsAsync(conn, today, tomorrow);
        var heatmap = await GetHeatmapAsync(conn, today);
        var categories = await GetCategoriesAsync(conn, today, tomorrow);
        var live = new LiveStatusDto(
            LiveStatusStore.CurrentApp,
            LiveStatusStore.IdleSeconds / 60,
            LiveStatusStore.IsIdle,
            LiveStatusStore.AudibleTab,
            LiveStatusStore.AudioActive,
            LiveStatusStore.SystemState,
            LiveStatusStore.PendingIdleReturn
        );

        var result = new DashboardResponse(summary, timeline, topApps, heatmap, categories, live);

        lock (_cacheLock)
        {
            _cache[cacheKey] = (result, DateTime.UtcNow);
        }

        return result;
    }

    private static async Task<SummaryDto> GetSummaryAsync(
        SqliteConnection conn, DateTime today, DateTime tomorrow, DateTime yesterday)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                COALESCE(SUM(CASE WHEN session_state = 'active' AND COALESCE(category, '') != 'system' THEN
                    (julianday(COALESCE(end_time, $now)) - julianday(start_time)) * 86400
                ELSE 0 END), 0) AS active_secs,
                COALESCE(SUM(CASE WHEN session_state IN ('idle', 'away') THEN
                    (julianday(COALESCE(end_time, $now)) - julianday(start_time)) * 86400
                ELSE 0 END), 0) AS idle_secs
            FROM app_events
            WHERE start_time >= $today AND start_time < $tomorrow
            """;
        cmd.Parameters.AddWithValue("$today", today.ToString("o"));
        cmd.Parameters.AddWithValue("$tomorrow", tomorrow.ToString("o"));
        var now = DateTime.UtcNow.ToString("o");
        cmd.Parameters.AddWithValue("$now", now);

        int activeSecs = 0, idleSecs = 0;
        using (var r = await cmd.ExecuteReaderAsync())
        {
            if (await r.ReadAsync())
            {
                activeSecs = Convert.ToInt32(r["active_secs"]);
                idleSecs = Convert.ToInt32(r["idle_secs"]);
            }
        }

        // Sum active time in productive categories for focus score
        cmd.CommandText = """
            SELECT COALESCE(SUM(
                (julianday(COALESCE(end_time, $now)) - julianday(start_time)) * 86400
            ), 0) FROM app_events
            WHERE start_time >= $today AND start_time < $tomorrow
              AND session_state = 'active'
              AND category IN ('development', 'work', 'documents', 'communication', 'design')
            """;
        var productiveSecs = Convert.ToInt32(await cmd.ExecuteScalarAsync());

        cmd.Parameters.AddWithValue("$yday", yesterday.ToString("o"));

        cmd.CommandText = """
            SELECT COUNT(*) FROM app_events
            WHERE start_time >= $yday AND start_time < $today
            """;
        var hadYesterdayData = Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;

        if (hadYesterdayData)
        {
            cmd.CommandText = """
                SELECT COALESCE(SUM(
                    (julianday(COALESCE(end_time, $now)) - julianday(start_time)) * 86400
                ), 0) FROM app_events
                WHERE start_time >= $yday AND start_time < $today AND session_state = 'active'
                """;
        }
        var yesterdaySecs = hadYesterdayData ? Convert.ToInt32(await cmd.ExecuteScalarAsync()) : -1;

        cmd.CommandText = """
            SELECT category, COALESCE(SUM(
                (julianday(COALESCE(end_time, $now)) - julianday(start_time)) * 86400
            ), 0) AS secs FROM app_events
            WHERE start_time >= $today AND start_time < $tomorrow AND session_state = 'active' AND category != 'system'
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

        var focusScore = activeSecs > 0 ? (int)Math.Round((double)productiveSecs / activeSecs * 100) : 0;

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
            yesterdaySecs >= 0 ? (activeSecs - yesterdaySecs) / 60 : null,
            totalKeys,
            totalClicks
        );
    }

    private static async Task<TimelineBlockDto[]> GetTimelineAsync(
        SqliteConnection conn, DateTime today, DateTime tomorrow)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT exe_name, window_title, category, start_time, end_time, was_idle, session_state
            FROM app_events
            WHERE start_time >= $today AND start_time < $tomorrow
              AND COALESCE(category, '') != 'system'
              AND (julianday(COALESCE(end_time, $now)) - julianday(start_time)) * 86400 >= 5
            ORDER BY start_time
            """;
        cmd.Parameters.AddWithValue("$today", today.ToString("o"));
        cmd.Parameters.AddWithValue("$tomorrow", tomorrow.ToString("o"));
        cmd.Parameters.AddWithValue("$now", DateTime.Now.ToString("o"));

        var blocks = new List<TimelineBlockDto>();
        using var r = await cmd.ExecuteReaderAsync();

        while (await r.ReadAsync())
        {
            var start = DateTime.SpecifyKind(DateTime.Parse(r.GetString(3)), DateTimeKind.Utc);
            var endStr = r.IsDBNull(4) ? null : r.GetString(4);
            var end = endStr is not null
                ? DateTime.SpecifyKind(DateTime.Parse(endStr), DateTimeKind.Utc)
                : DateTime.UtcNow;
            var sessionState = r.IsDBNull(6) ? (r.GetInt32(5) == 1 ? "idle" : "active") : r.GetString(6);
            var cat = r.IsDBNull(2) ? null : r.GetString(2);

            var localStart = TimeZoneInfo.ConvertTimeFromUtc(start, TimeZoneInfo.Local);
            var localEnd = TimeZoneInfo.ConvertTimeFromUtc(end, TimeZoneInfo.Local);

            var startHour = localStart.TimeOfDay.TotalHours;
            var endHour = localEnd.Date > localStart.Date ? 24.0 : localEnd.TimeOfDay.TotalHours;

            var type = sessionState == "active" ? (cat ?? "other") : sessionState;

            // Merge adjacent blocks of same type
            if (blocks.Count > 0 && blocks[^1].Type == type &&
                Math.Abs(blocks[^1].EndHour - startHour) < 0.01)
            {
                blocks[^1] = blocks[^1] with { EndHour = endHour };
            }
            else
            {
                blocks.Add(new TimelineBlockDto(startHour, endHour, type));
            }
        }

        return blocks.ToArray();
    }

    private static async Task<TopAppDto[]> GetTopAppsAsync(
        SqliteConnection conn, DateTime today, DateTime tomorrow)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT exe_name, COALESCE(SUM(
                (julianday(COALESCE(end_time, $now)) - julianday(start_time)) * 86400
            ), 0) AS secs FROM app_events
            WHERE start_time >= $today AND start_time < $tomorrow
              AND session_state = 'active' AND COALESCE(category, '') != 'system'
            GROUP BY exe_name ORDER BY secs DESC LIMIT 8
            """;
        cmd.Parameters.AddWithValue("$today", today.ToString("o"));
        cmd.Parameters.AddWithValue("$tomorrow", tomorrow.ToString("o"));
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));

        var apps = new List<TopAppDto>();
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var secs = Convert.ToInt32(r["secs"]);
            apps.Add(new TopAppDto(r.GetString(0), secs / 60));
        }
        return apps.ToArray();
    }

    private static async Task<HeatmapEntryDto[]> GetHeatmapAsync(
        SqliteConnection conn, DateTime today)
    {
        var startDate = today.AddDays(-27);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT DATE(start_time) AS day,
                   COALESCE(SUM(CASE WHEN session_state = 'active' AND COALESCE(category, '') != 'system' THEN
                       (julianday(COALESCE(end_time, DATE(start_time, '+1 day'))) -
                        julianday(start_time)) * 86400
                   ELSE 0 END), 0) AS secs
            FROM app_events
            WHERE start_time >= $start
            GROUP BY DATE(start_time)
            ORDER BY day
            """;
        cmd.Parameters.AddWithValue("$start", startDate.ToString("o"));

        var map = new Dictionary<string, int>();
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var day = r.GetString(0);
            var secs = Convert.ToInt32(r["secs"]);
            map[day] = secs / 3600;
        }

        var entries = new List<HeatmapEntryDto>();
        for (int i = 0; i < 28; i++)
        {
            var date = startDate.AddDays(i).ToString("yyyy-MM-dd");
            entries.Add(new HeatmapEntryDto(date, map.GetValueOrDefault(date, 0)));
        }
        return entries.ToArray();
    }

    private static async Task<CategoryEntryDto[]> GetCategoriesAsync(
        SqliteConnection conn, DateTime today, DateTime tomorrow)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COALESCE(category, 'other') AS cat, COALESCE(SUM(
                (julianday(COALESCE(end_time, $now)) - julianday(start_time)) * 86400
            ), 0) AS secs FROM app_events
            WHERE start_time >= $today AND start_time < $tomorrow
              AND session_state = 'active' AND COALESCE(category, '') != 'system'
            GROUP BY cat ORDER BY secs DESC
            """;
        cmd.Parameters.AddWithValue("$today", today.ToString("o"));
        cmd.Parameters.AddWithValue("$tomorrow", tomorrow.ToString("o"));
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));

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
        SqliteConnection conn, DateTime today, DateTime tomorrow)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT domain,
                   COUNT(*) AS visits,
                   MAX(start_time) AS last_visit
            FROM browser_events
            WHERE start_time >= $today AND start_time < $tomorrow
            GROUP BY domain ORDER BY visits DESC LIMIT 20
            """;
        cmd.Parameters.AddWithValue("$today", today.ToString("o"));
        cmd.Parameters.AddWithValue("$tomorrow", tomorrow.ToString("o"));

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

    private static string FormatDuration(int totalSecs)
    {
        var h = totalSecs / 3600;
        var m = (totalSecs % 3600) / 60;
        return $"{h}h {m:D2}m";
    }
}
