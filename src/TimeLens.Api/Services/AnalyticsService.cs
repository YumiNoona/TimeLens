using Microsoft.Data.Sqlite;
using TimeLens.Api.Dtos;


namespace TimeLens.Api.Services;

public sealed class AnalyticsService
{
    private readonly string _connString;

    public AnalyticsService(string dbPath)
    {
        _connString = $"Data Source={dbPath}";
    }

    public async Task<DashboardResponse> GetDashboardAsync(DateTime? queryDate = null)
    {
        using var conn = new SqliteConnection(_connString);
        await conn.OpenAsync();

        var localDate = (queryDate ?? DateTime.Now).Date;
        var today = localDate.ToUniversalTime();
        var tomorrow = localDate.AddDays(1).ToUniversalTime();
        var yesterday = localDate.AddDays(-1).ToUniversalTime();

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
            LiveStatusStore.AudioActive
        );

        return new DashboardResponse(summary, timeline, topApps, heatmap, categories, live);
    }

    private static async Task<SummaryDto> GetSummaryAsync(
        SqliteConnection conn, DateTime today, DateTime tomorrow, DateTime yesterday)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                COALESCE(SUM(CASE WHEN was_idle = 0 THEN
                    (julianday(COALESCE(end_time, datetime('now'))) - julianday(start_time)) * 86400
                ELSE 0 END), 0) AS active_secs,
                COALESCE(SUM(CASE WHEN was_idle = 1 THEN
                    (julianday(COALESCE(end_time, datetime('now'))) - julianday(start_time)) * 86400
                ELSE 0 END), 0) AS idle_secs
            FROM app_events
            WHERE start_time >= $today AND start_time < $tomorrow
            """;
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

        cmd.CommandText = """
            SELECT COALESCE(SUM(
                (julianday(COALESCE(end_time, datetime('now'))) - julianday(start_time)) * 86400
            ), 0) FROM app_events
            WHERE start_time >= $yday AND start_time < $today AND was_idle = 0
            """;
        cmd.Parameters.AddWithValue("$yday", yesterday.ToString("o"));
        var yesterdaySecs = Convert.ToInt32(await cmd.ExecuteScalarAsync());

        cmd.CommandText = """
            SELECT category, COALESCE(SUM(
                (julianday(COALESCE(end_time, datetime('now'))) - julianday(start_time)) * 86400
            ), 0) AS secs FROM app_events
            WHERE start_time >= $today AND start_time < $tomorrow AND was_idle = 0
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

        var total = activeSecs + idleSecs;
        var focusScore = total > 0 ? (int)Math.Round((double)activeSecs / total * 100) : 0;

        return new SummaryDto(
            FormatDuration(activeSecs), activeSecs,
            FormatDuration(idleSecs), idleSecs,
            focusScore,
            topCat, FormatDuration(topCatSecs),
            (activeSecs - yesterdaySecs) / 60
        );
    }

    private static async Task<TimelineBlockDto[]> GetTimelineAsync(
        SqliteConnection conn, DateTime today, DateTime tomorrow)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT exe_name, window_title, category, start_time, end_time, was_idle
            FROM app_events
            WHERE start_time >= $today AND start_time < $tomorrow
            ORDER BY start_time
            """;
        cmd.Parameters.AddWithValue("$today", today.ToString("o"));
        cmd.Parameters.AddWithValue("$tomorrow", tomorrow.ToString("o"));

        var blocks = new List<TimelineBlockDto>();
        using var r = await cmd.ExecuteReaderAsync();

        while (await r.ReadAsync())
        {
            var start = DateTime.Parse(r.GetString(3));
            var endStr = r.IsDBNull(4) ? null : r.GetString(4);
            var end = endStr is not null ? DateTime.Parse(endStr) : DateTime.UtcNow;
            var wasIdle = r.GetInt32(5) == 1;
            var cat = r.IsDBNull(2) ? null : r.GetString(2);

            var startHour = start.TimeOfDay.TotalHours;
            var endHour = end.Date > start.Date ? 24.0 : end.TimeOfDay.TotalHours;

            var type = wasIdle ? "idle" : cat ?? "other";

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
                (julianday(COALESCE(end_time, datetime('now'))) - julianday(start_time)) * 86400
            ), 0) AS secs FROM app_events
            WHERE start_time >= $today AND start_time < $tomorrow AND was_idle = 0
            GROUP BY exe_name ORDER BY secs DESC LIMIT 8
            """;
        cmd.Parameters.AddWithValue("$today", today.ToString("o"));
        cmd.Parameters.AddWithValue("$tomorrow", tomorrow.ToString("o"));

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
                   COALESCE(SUM(CASE WHEN was_idle = 0 THEN
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
                (julianday(COALESCE(end_time, datetime('now'))) - julianday(start_time)) * 86400
            ), 0) AS secs FROM app_events
            WHERE start_time >= $today AND start_time < $tomorrow AND was_idle = 0
            GROUP BY cat ORDER BY secs DESC
            """;
        cmd.Parameters.AddWithValue("$today", today.ToString("o"));
        cmd.Parameters.AddWithValue("$tomorrow", tomorrow.ToString("o"));

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

    private static string FormatDuration(int totalSecs)
    {
        var h = totalSecs / 3600;
        var m = (totalSecs % 3600) / 60;
        return $"{h}h {m:D2}m";
    }
}
