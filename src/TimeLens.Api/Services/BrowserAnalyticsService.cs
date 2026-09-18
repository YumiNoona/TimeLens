using System.Globalization;
using Microsoft.Data.Sqlite;
using TimeLens.Api.Dtos;

namespace TimeLens.Api.Services;

public static class BrowserAnalyticsService
{
    public sealed record BrowserVisitDetail(string Domain, string Url, string Title, string Browser,
        string StartedAt, string EndedAt, double ActiveSeconds);
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
        // Use the same repaired, idle-free foreground ledger as Apps, Timeline,
        // categories and exports. A stale browser row must never keep claiming
        // website time after a newer foreground application supersedes it.
        var active = await ActivityIntervalService.ReadExclusiveAsync(conn, dayStart, dayEnd);
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
            var eligible = active.Where(a => BrowserTrackingService.MatchesForeground(v.Browser, a.ExeName))
                .Select(a => (Start: a.StartUtc > start ? a.StartUtc : start, End: a.EndUtc < end ? a.EndUtc : end))
                .Where(a => a.End > a.Start).OrderBy(a => a.Start).ToArray();
            double seconds = 0;
            var cursor = start;
            foreach (var span in eligible)
            {
                var from = span.Start > cursor ? span.Start : cursor;
                var to = span.End;
                if (to <= from) continue;
                seconds += (to - from).TotalSeconds;
                recordInterval?.Invoke(from, to);
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

    public static async Task<BrowserVisitDetail[]> ReadVisitsAsync(SqliteConnection conn, DateTime dayStart, DateTime dayEnd)
    {
        var active = await ActivityIntervalService.ReadExclusiveAsync(conn, dayStart, dayEnd);
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
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) visits.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), Parse(reader.GetString(4)), Parse(reader.GetString(5))));
        }
        var result = new List<BrowserVisitDetail>();
        for (var i = 0; i < visits.Count; i++)
        {
            var visit = visits[i];
            var start = visit.Start > dayStart ? visit.Start : dayStart;
            var end = visit.End < dayEnd ? visit.End : dayEnd;
            if (i + 1 < visits.Count && visits[i + 1].Start < end) end = visits[i + 1].Start;
            if (end <= start) continue;
            double seconds = 0;
            var cursor = start;
            foreach (var foreground in active.Where(a => BrowserTrackingService.MatchesForeground(visit.Browser, a.ExeName)).OrderBy(a => a.StartUtc))
            {
                var from = foreground.StartUtc > cursor ? foreground.StartUtc : cursor;
                var to = foreground.EndUtc < end ? foreground.EndUtc : end;
                if (to <= from) continue;
                seconds += (to - from).TotalSeconds;
                cursor = to;
            }
            if (seconds > 0) result.Add(new(visit.Domain, visit.Url, visit.Title, visit.Browser,
                start.ToString("o"), end.ToString("o"), seconds));
        }
        return result.OrderByDescending(v => v.StartedAt).ToArray();
    }
}
