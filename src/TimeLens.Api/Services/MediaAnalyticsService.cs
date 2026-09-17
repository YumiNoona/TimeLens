using System.Globalization;
using Microsoft.Data.Sqlite;
using TimeLens.Api.Dtos;

namespace TimeLens.Api.Services;

public static class MediaAnalyticsService
{
    private sealed record Span(DateTime Start, DateTime End);
    private sealed record WebRow(string Domain, string Title, string Browser, string Kind,
        string Visibility, string Confidence, DateTime Start, DateTime End);

    public static async Task<WebMediaSummaryDto> ReadWebAsync(
        SqliteConnection conn, DateTime startUtc, DateTime endUtc)
    {
        var rows = new List<WebRow>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT domain,title,browser,media_kind,visibility,confidence,start_time,end_time
            FROM web_media_events
            WHERE julianday(start_time) < julianday($end) AND julianday(end_time) > julianday($start)
            ORDER BY start_time
            """;
        cmd.Parameters.AddWithValue("$start", startUtc.ToString("o"));
        cmd.Parameters.AddWithValue("$end", endUtc.ToString("o"));
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var start = ParseUtc(reader.GetString(6));
            var end = ParseUtc(reader.GetString(7));
            start = start < startUtc ? startUtc : start;
            end = end > endUtc ? endUtc : end;
            if (end <= start) continue;
            rows.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), start, end));
        }

        var entries = rows.GroupBy(x => new { x.Domain, x.Title, x.Browser, x.Kind, x.Confidence })
            .Select(group => new WebMediaEntryDto(group.Key.Domain, group.Key.Title,
                group.Key.Browser, group.Key.Kind,
                (int)Math.Round(Union(group.Select(x => new Span(x.Start, x.End)))),
                (int)Math.Round(Union(group.Where(x => x.Visibility == "background")
                    .Select(x => new Span(x.Start, x.End)))),
                (int)Math.Round(Union(group.Where(x => x.Visibility == "pip")
                    .Select(x => new Span(x.Start, x.End)))),
                group.Count(), group.Key.Confidence))
            .OrderByDescending(x => x.PlaybackSeconds).ToArray();

        return new WebMediaSummaryDto(
            (int)Math.Round(Union(rows.Select(x => new Span(x.Start, x.End)))),
            (int)Math.Round(Union(rows.Where(x => x.Visibility == "background")
                .Select(x => new Span(x.Start, x.End)))),
            (int)Math.Round(Union(rows.Where(x => x.Visibility == "pip")
                .Select(x => new Span(x.Start, x.End)))), entries);
    }

    public static async Task<AudioSessionDto[]> ReadNativeAsync(
        SqliteConnection conn, DateTime startUtc, DateTime endUtc, DateTime nowUtc)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COALESCE(pid,0),COALESCE(exe_name,''),timestamp,is_playing
            FROM audio_activity
            WHERE timestamp >= $lookback AND timestamp < $end
            ORDER BY timestamp,id
            """;
        cmd.Parameters.AddWithValue("$lookback", startUtc.AddMinutes(-1).ToString("o"));
        cmd.Parameters.AddWithValue("$end", endUtc.ToString("o"));
        var events = new List<(int Pid, string Exe, DateTime At, bool Playing)>();
        using (var reader = await cmd.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                events.Add((reader.GetInt32(0), reader.GetString(1), ParseUtc(reader.GetString(2)), reader.GetInt32(3) == 1));

        var output = new List<AudioSessionDto>();
        foreach (var group in events.Where(x => x.Exe.Length > 0).GroupBy(x => (x.Pid, x.Exe)))
        {
            DateTime? open = null;
            var spans = new List<Span>();
            var sessions = 0;
            foreach (var evt in group)
            {
                if (evt.Playing)
                {
                    if (open is null) { open = evt.At; sessions++; }
                }
                else if (open is not null)
                {
                    AddClipped(spans, open.Value, evt.At, startUtc, endUtc);
                    open = null;
                }
            }
            if (open is not null)
            {
                // Checkpoints arrive every 15s. Never infer an unbounded session after
                // a crash; only grant a short lease beyond the last observation.
                var lastTrue = group.Where(x => x.Playing).Max(x => x.At);
                var leaseEnd = new[] { endUtc, nowUtc, lastTrue.AddSeconds(20) }.Min();
                AddClipped(spans, open.Value, leaseEnd, startUtc, endUtc);
            }
            var seconds = (int)Math.Round(Union(spans));
            if (seconds > 0)
                output.Add(new(group.Key.Exe, sessions, spans.Min(x => x.Start).ToString("o"), seconds));
        }
        return output.OrderByDescending(x => x.PlaybackSeconds).ToArray();
    }

    private static void AddClipped(List<Span> spans, DateTime start, DateTime end,
        DateTime rangeStart, DateTime rangeEnd)
    {
        start = start < rangeStart ? rangeStart : start;
        end = end > rangeEnd ? rangeEnd : end;
        if (end > start) spans.Add(new(start, end));
    }

    private static double Union(IEnumerable<Span> source)
    {
        var spans = source.OrderBy(x => x.Start).ToArray();
        if (spans.Length == 0) return 0;
        var total = 0d;
        var start = spans[0].Start;
        var end = spans[0].End;
        foreach (var span in spans.Skip(1))
        {
            if (span.Start <= end) end = span.End > end ? span.End : end;
            else { total += (end - start).TotalSeconds; start = span.Start; end = span.End; }
        }
        return total + (end - start).TotalSeconds;
    }

    private static DateTime ParseUtc(string value) => DateTime.Parse(value, null,
        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}
