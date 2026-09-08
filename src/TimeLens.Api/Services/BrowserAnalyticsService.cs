using System.Globalization;
using Microsoft.Data.Sqlite;
using TimeLens.Api.Dtos;

namespace TimeLens.Api.Services;

public static class BrowserAnalyticsService
{
    private sealed record Span(DateTime Start, DateTime End, string App);
    private sealed record Visit(string Domain, string Url, string Title, string Browser, DateTime Start, DateTime End);
    private sealed class Page(string domain, string url, string browser)
    {
        public string Domain = domain, Url = url, Browser = browser, Title = "";
        public DateTime LastSeen;
        public double Seconds;
        public int Sessions, Keys, Clicks;
        public bool HasInput;
    }
    private static DateTime Parse(string value) => DateTime.Parse(value, null,
        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    public static async Task<BrowserEntryDto[]> ReadAsync(SqliteConnection conn, DateTime dayStart, DateTime dayEnd, Action<DateTime, DateTime>? recordInterval = null)
    {
        var active = new List<Span>();
        var idle = new List<Span>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT start_time,COALESCE(end_time,start_time),exe_name FROM app_events
                WHERE session_state='active' AND julianday(start_time)<julianday($end)
                  AND julianday(COALESCE(end_time,start_time))>julianday($start)
                """;
            cmd.Parameters.AddWithValue("$start", dayStart.ToString("o"));
            cmd.Parameters.AddWithValue("$end", dayEnd.ToString("o"));
            using (var r = await cmd.ExecuteReaderAsync())
                while (await r.ReadAsync()) active.Add(new(Parse(r.GetString(0)), Parse(r.GetString(1)), r.GetString(2)));
            cmd.CommandText = """
                SELECT start_time,COALESCE(end_time,start_time) FROM idle_spans
                WHERE julianday(start_time)<julianday($end) AND julianday(COALESCE(end_time,start_time))>julianday($start)
                """;
            using var ir = await cmd.ExecuteReaderAsync();
            while (await ir.ReadAsync()) idle.Add(new(Parse(ir.GetString(0)), Parse(ir.GetString(1)), ""));
        }
        var visits = new List<Visit>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT domain,COALESCE(url,''),COALESCE(title,''),browser,start_time,COALESCE(end_time,start_time)
                FROM browser_events WHERE julianday(start_time)<julianday($end)
                  AND julianday(COALESCE(end_time,start_time))>julianday($start)
                ORDER BY julianday(start_time),id
                """;
            cmd.Parameters.AddWithValue("$start", dayStart.ToString("o"));
            cmd.Parameters.AddWithValue("$end", dayEnd.ToString("o"));
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) visits.Add(new(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), Parse(r.GetString(4)), Parse(r.GetString(5))));
        }
        var pages = new Dictionary<(string Url, string Browser), Page>();
        Page GetPage(string domain, string url, string browser)
        {
            var key = (url, browser);
            if (!pages.TryGetValue(key, out var page)) pages[key] = page = new(domain, url, browser);
            return page;
        }
        for (var i = 0; i < visits.Count; i++)
        {
            var v = visits[i];
            var start = v.Start > dayStart ? v.Start : dayStart;
            var end = v.End < dayEnd ? v.End : dayEnd;
            // Earlier versions left several tabs open. The next observed selection
            // supersedes the previous one, so those rows cannot multiply machine time.
            if (i + 1 < visits.Count && visits[i + 1].Start < end) end = visits[i + 1].Start;
            if (end <= start) continue;
            var eligible = active.Where(a => BrowserTrackingService.MatchesForeground(v.Browser, a.App))
                .Select(a => (Start: a.Start > start ? a.Start : start, End: a.End < end ? a.End : end))
                .Where(a => a.End > a.Start).OrderBy(a => a.Start).ToArray();
            double seconds = 0;
            var cursor = start;
            foreach (var span in eligible)
            {
                var from = span.Start > cursor ? span.Start : cursor;
                var to = span.End;
                if (to <= from) continue;
                var idleCursor = from;
                foreach (var away in idle.Where(a => a.End > from && a.Start < to).OrderBy(a => a.Start))
                {
                    var awayStart = away.Start > idleCursor ? away.Start : idleCursor;
                    if (awayStart > idleCursor) { seconds += (awayStart - idleCursor).TotalSeconds; recordInterval?.Invoke(idleCursor, awayStart); }
                    if (away.End > idleCursor) idleCursor = away.End < to ? away.End : to;
                }
                if (to > idleCursor) { seconds += (to - idleCursor).TotalSeconds; recordInterval?.Invoke(idleCursor, to); }
                cursor = to;
            }
            if (seconds <= 0) continue;
            var page = GetPage(v.Domain, v.Url, v.Browser);
            page.Seconds += seconds;
            page.Sessions++;
            if (end >= page.LastSeen) { page.LastSeen = end; page.Title = v.Title; }
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT domain,url,title,browser,timestamp,keystrokes,clicks FROM browser_input_batches
                WHERE timestamp >= $start AND timestamp < $end ORDER BY timestamp
                """;
            cmd.Parameters.AddWithValue("$start", dayStart.ToString("o"));
            cmd.Parameters.AddWithValue("$end", dayEnd.ToString("o"));
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var page = GetPage(r.GetString(0), r.GetString(1), r.GetString(3));
                page.HasInput = true;
                page.Keys += r.GetInt32(5); page.Clicks += r.GetInt32(6);
                var seen = Parse(r.GetString(4));
                if (seen >= page.LastSeen) { page.LastSeen = seen; page.Title = r.GetString(2); }
            }
        }
        return pages.Values.GroupBy(p => p.Domain, StringComparer.OrdinalIgnoreCase)
            .Select(g => new BrowserEntryDto(g.Key, g.Sum(p => p.Sessions), g.Max(p => p.LastSeen).ToString("o"),
                g.Sum(p => p.Seconds), g.Any(p => p.HasInput) ? g.Sum(p => p.Keys) : null,
                g.Any(p => p.HasInput) ? g.Sum(p => p.Clicks) : null,
                g.OrderByDescending(p => p.Seconds).Select(p => new BrowserPageDto(p.Url, p.Title, p.Browser,
                    p.Seconds, p.Sessions, p.LastSeen.ToString("o"), p.HasInput ? p.Keys : null, p.HasInput ? p.Clicks : null)).ToArray()))
            .OrderByDescending(s => s.TotalSeconds).ToArray();
    }
}
