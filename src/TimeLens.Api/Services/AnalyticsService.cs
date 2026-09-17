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
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    private static readonly TimeSpan CacheTtlToday = TimeSpan.FromSeconds(2);

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

        await _refreshGate.WaitAsync();
        try
        {
            if ((isToday || isYesterday) &&
                _cache.TryGetValue(cacheKey, out var entry) && DateTime.UtcNow - entry.cachedAt < CacheTtlToday)
                return entry.data;

            using var conn = new SqliteConnection(_connString);
            await conn.OpenAsync();
            using var snapshot = conn.BeginTransaction(deferred: true);

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

            var browserSites = await BrowserAnalyticsService.ReadAsync(conn, today, rangeEnd);
            var audioSessions = await MediaAnalyticsService.ReadNativeAsync(conn, today, tomorrow, DateTime.UtcNow);
            var webMedia = await MediaAnalyticsService.ReadWebAsync(conn, today, rangeEnd);

            var result = new DashboardResponse(summary, timeline, topApps, heatmap, categories,
                live, browserSites, audioSessions, webMedia);

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
            _refreshGate.Release();
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
        var segments = await ActivityIntervalService.ReadExclusiveAsync(conn, today, rangeEnd);
        var previousStart = today.ToLocalTime().Date.AddDays(-1).ToUniversalTime();
        var previousSegments = await ActivityIntervalService.ReadExclusiveAsync(conn, previousStart, today);
        var activeSecs = (int)Math.Round(segments.Sum(x => x.Seconds));
        var idleSecs = (int)Math.Round(await ActivityIntervalService.ReadIdleUnionSecondsAsync(conn, today, rangeEnd));
        var productiveSecs = (int)Math.Round(segments
            .Where(x => x.Category is "development" or "work" or "documents" or "communication" or "design")
            .Sum(x => x.Seconds));
        var otherSecs = (int)Math.Round(segments.Where(x => x.Category == "other").Sum(x => x.Seconds));
        var yesterdaySecs = previousSegments.Length > 0
            ? (int)Math.Round(previousSegments.Sum(x => x.Seconds))
            : -1;
        var topCategory = segments.GroupBy(x => x.Category)
            .Select(x => new { Name = x.Key, Seconds = x.Sum(y => y.Seconds) })
            .OrderByDescending(x => x.Seconds).FirstOrDefault();
        var topCat = topCategory?.Name ?? "—";
        var topCatSecs = (int)Math.Round(topCategory?.Seconds ?? 0);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1";
        cmd.Parameters.AddWithValue("$date", localDate);
        cmd.Parameters.AddWithValue("$rangeEnd", rangeEnd.ToString("o"));
        cmd.Parameters.AddWithValue("$previousEnd", today.ToString("o"));
        cmd.Parameters.AddWithValue("$previousStart", today.ToLocalTime().Date.AddDays(-1).ToUniversalTime().ToString("o"));
        cmd.Parameters.AddWithValue("$today", today.ToString("o"));
        cmd.Parameters.AddWithValue("$tomorrow", tomorrow.ToString("o"));

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
            LogQueryError($"{DateTime.UtcNow:o} input_totals: {ex}{Environment.NewLine}");
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

    private static void LogQueryError(string message)
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TimeLens", "query_error.log");
            if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024)
                File.Move(path, path + ".previous", true);
            File.AppendAllText(path, message);
        }
        catch { }
    }

    private static async Task<TimelineBlockDto[]> GetTimelineAsync(
        SqliteConnection conn, string localDate, DateTime localEndOfDayUtc)
    {
        var localStartOfDayUtc = DateTime.SpecifyKind(DateTime.ParseExact(localDate, "yyyy-MM-dd", null), DateTimeKind.Local).ToUniversalTime();
        var blocks = new List<TimelineBlockDto>();
        var segments = await ActivityIntervalService.ReadExclusiveAsync(conn, localStartOfDayUtc, localEndOfDayUtc);
        foreach (var segment in segments)
        {
            var localStart = TimeZoneInfo.ConvertTimeFromUtc(segment.StartUtc, TimeZoneInfo.Local);
            var localEnd = TimeZoneInfo.ConvertTimeFromUtc(segment.EndUtc, TimeZoneInfo.Local);

            var startHour = localStart.TimeOfDay.TotalHours;
            var endHour = localEnd.Date > localStart.Date ? 24.0 : localEnd.TimeOfDay.TotalHours;

            if (endHour <= startHour) continue;
            var durationSecs = (int)Math.Round(segment.Seconds);
            var type = segment.Category;

            if (blocks.Count > 0 && blocks[^1].Type == type &&
                string.Equals(blocks[^1].ExeName, segment.ExeName, StringComparison.OrdinalIgnoreCase) &&
                blocks[^1].WindowTitle == segment.WindowTitle && blocks[^1].Project == segment.Project &&
                Math.Abs(blocks[^1].EndHour - startHour) < 0.001 / 3600.0)
            {
                blocks[^1] = blocks[^1] with { EndHour = endHour, DurationSeconds = blocks[^1].DurationSeconds + durationSecs };
            }
            else
            {
                blocks.Add(new TimelineBlockDto(startHour, endHour, type, segment.ExeName,
                    segment.WindowTitle, durationSecs, segment.Project));
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

        return blocks.ToArray();
    }

    private static async Task<TopAppDto[]> GetTopAppsAsync(
        SqliteConnection conn, string localDate, DateTime today, DateTime tomorrow, DateTime rangeEnd)
    {
        var segments = await ActivityIntervalService.ReadExclusiveAsync(conn, today, rangeEnd);
        var durations = segments.GroupBy(x => x.ExeName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.Seconds), StringComparer.OrdinalIgnoreCase);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT exe_name, COALESCE(SUM(keystroke_count),0), COALESCE(SUM(click_count),0)
            FROM input_activity
            WHERE timestamp >= $t0 AND timestamp < $t1 AND exe_name IS NOT NULL
            GROUP BY exe_name
            """;
        cmd.Parameters.AddWithValue("$date", localDate);
        cmd.Parameters.AddWithValue("$now", rangeEnd.ToString("o"));
        cmd.Parameters.AddWithValue("$t0", today.ToString("o"));
        cmd.Parameters.AddWithValue("$t1", tomorrow.ToString("o"));

        var inputs = new Dictionary<string, (int Keys, int Clicks)>(StringComparer.OrdinalIgnoreCase);
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            inputs[r.GetString(0)] = (r.GetInt32(1), r.GetInt32(2));
        return durations.Select(x => new TopAppDto(x.Key, x.Value / 60.0,
                inputs.GetValueOrDefault(x.Key).Keys, inputs.GetValueOrDefault(x.Key).Clicks))
            .OrderByDescending(x => x.Minutes).ToArray();
    }

    private static async Task<HeatmapEntryDto[]> GetHeatmapAsync(
        SqliteConnection conn, DateTime localDate, DateTime rangeEnd)
    {
        var startDate = localDate.AddDays(-(HeatmapDays - 1));
        var rangeStart = startDate.ToUniversalTime();
        var seconds = new Dictionary<string, double>();
        var segments = await ActivityIntervalService.ReadExclusiveAsync(conn, rangeStart, rangeEnd);
        foreach (var segment in segments)
        {
            var start = segment.StartUtc;
            var end = segment.EndUtc;
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
        var map = seconds.ToDictionary(x => x.Key, x => x.Value / 60.0);

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
        var segments = await ActivityIntervalService.ReadExclusiveAsync(conn, today, rangeEnd);
        var cats = segments.GroupBy(x => x.Category)
            .Select(x => new CategoryEntryDto(x.Key, 0, x.Sum(y => y.Seconds) / 60.0))
            .OrderByDescending(x => x.Minutes).ToList();
        var totalSecs = cats.Sum(x => x.Minutes * 60);

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
        => await MediaAnalyticsService.ReadNativeAsync(conn, today, tomorrow, DateTime.UtcNow);

    private static string FormatDuration(int totalSecs)
    {
        var h = totalSecs / 3600;
        var m = (totalSecs % 3600) / 60;
        return $"{h}h {m:D2}m";
    }
}
