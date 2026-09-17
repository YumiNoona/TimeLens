using System.Globalization;
using Microsoft.Data.Sqlite;

namespace TimeLens.Api.Services;

/// <summary>
/// Converts possibly overlapping historical rows into one exclusive foreground
/// timeline. Idle/away spans win over applications and are not attributed to the
/// window that happened to be visible when input stopped.
/// </summary>
public static class ActivityIntervalService
{
    public sealed record Segment(
        long SourceId,
        string ExeName,
        string? WindowTitle,
        string Category,
        string? Project,
        DateTime StartUtc,
        DateTime EndUtc)
    {
        public double Seconds => Math.Max(0, (EndUtc - StartUtc).TotalSeconds);
    }

    private sealed record Candidate(long Id, string Exe, string? Title, string Category,
        string? Project, DateTime Start, DateTime End);
    private sealed record Span(DateTime Start, DateTime End);

    public static async Task<Segment[]> ReadExclusiveAsync(
        SqliteConnection conn, DateTime rangeStartUtc, DateTime rangeEndUtc)
    {
        if (rangeEndUtc <= rangeStartUtc) return [];

        var apps = new List<Candidate>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT id, exe_name, window_title, COALESCE(category,'other'),
                       COALESCE(project,''), start_time, COALESCE(end_time,$end)
                FROM app_events
                WHERE session_state='active'
                  AND julianday(start_time) < julianday($end)
                  AND julianday(COALESCE(end_time,$end)) > julianday($start)
                ORDER BY start_time, id
                """;
            cmd.Parameters.AddWithValue("$start", rangeStartUtc.ToString("o"));
            cmd.Parameters.AddWithValue("$end", rangeEndUtc.ToString("o"));
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var start = ParseUtc(reader.GetString(5));
                var end = ParseUtc(reader.GetString(6));
                start = start < rangeStartUtc ? rangeStartUtc : start;
                end = end > rangeEndUtc ? rangeEndUtc : end;
                if (end <= start) continue;
                apps.Add(new Candidate(reader.GetInt64(0), reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetString(3),
                    reader.GetString(4) is { Length: > 0 } project ? project : null, start, end));
            }
        }

        var idle = new List<Span>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT start_time, COALESCE(end_time,$end) FROM idle_spans
                WHERE julianday(start_time) < julianday($end)
                  AND julianday(COALESCE(end_time,$end)) > julianday($start)
                """;
            cmd.Parameters.AddWithValue("$start", rangeStartUtc.ToString("o"));
            cmd.Parameters.AddWithValue("$end", rangeEndUtc.ToString("o"));
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var start = ParseUtc(reader.GetString(0));
                var end = ParseUtc(reader.GetString(1));
                start = start < rangeStartUtc ? rangeStartUtc : start;
                end = end > rangeEndUtc ? rangeEndUtc : end;
                if (end > start) idle.Add(new Span(start, end));
            }
        }

        // A later foreground observation permanently supersedes an older open
        // row. This prevents a stale/crash-recovered row from "resuming" after
        // the newer window closes. Rows with the same start keep the newest id.
        var ordered = apps.GroupBy(x => x.Start)
            .Select(group => group.MaxBy(x => x.Id)!)
            .OrderBy(x => x.Start).ToArray();
        for (var i = 0; i + 1 < ordered.Length; i++)
            if (ordered[i + 1].Start < ordered[i].End)
                ordered[i] = ordered[i] with { End = ordered[i + 1].Start };

        idle.Sort((a, b) => a.Start.CompareTo(b.Start));
        var mergedIdle = new List<Span>();
        foreach (var span in idle)
        {
            if (mergedIdle.Count == 0 || span.Start > mergedIdle[^1].End) mergedIdle.Add(span);
            else if (span.End > mergedIdle[^1].End) mergedIdle[^1] = mergedIdle[^1] with { End = span.End };
        }

        var result = new List<Segment>();
        var idleIndex = 0;
        foreach (var row in ordered)
        {
            if (row.End <= row.Start) continue;
            while (idleIndex < mergedIdle.Count && mergedIdle[idleIndex].End <= row.Start) idleIndex++;
            var cursor = row.Start;
            var scan = idleIndex;
            while (scan < mergedIdle.Count && mergedIdle[scan].Start < row.End)
            {
                var away = mergedIdle[scan];
                if (away.Start > cursor) Add(row, cursor, away.Start < row.End ? away.Start : row.End);
                if (away.End > cursor) cursor = away.End;
                if (cursor >= row.End) break;
                scan++;
            }
            if (cursor < row.End) Add(row, cursor, row.End);
        }

        return result.ToArray();

        void Add(Candidate row, DateTime start, DateTime end)
        {
            if (end <= start) return;
            if (result.Count > 0 && result[^1].SourceId == row.Id && result[^1].EndUtc == start)
                result[^1] = result[^1] with { EndUtc = end };
            else
                result.Add(new Segment(row.Id, row.Exe, row.Title, row.Category, row.Project, start, end));
        }
    }

    public static async Task<double> ReadIdleUnionSecondsAsync(
        SqliteConnection conn, DateTime rangeStartUtc, DateTime rangeEndUtc)
    {
        var spans = new List<Span>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT start_time, COALESCE(end_time,$end) FROM idle_spans
            WHERE julianday(start_time) < julianday($end)
              AND julianday(COALESCE(end_time,$end)) > julianday($start)
            ORDER BY start_time
            """;
        cmd.Parameters.AddWithValue("$start", rangeStartUtc.ToString("o"));
        cmd.Parameters.AddWithValue("$end", rangeEndUtc.ToString("o"));
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var start = ParseUtc(reader.GetString(0));
            var end = ParseUtc(reader.GetString(1));
            start = start < rangeStartUtc ? rangeStartUtc : start;
            end = end > rangeEndUtc ? rangeEndUtc : end;
            if (end > start) spans.Add(new Span(start, end));
        }
        if (spans.Count == 0) return 0;
        spans.Sort((a, b) => a.Start.CompareTo(b.Start));
        var total = 0d;
        var currentStart = spans[0].Start;
        var currentEnd = spans[0].End;
        foreach (var span in spans.Skip(1))
        {
            if (span.Start <= currentEnd)
                currentEnd = span.End > currentEnd ? span.End : currentEnd;
            else
            {
                total += (currentEnd - currentStart).TotalSeconds;
                currentStart = span.Start;
                currentEnd = span.End;
            }
        }
        return total + (currentEnd - currentStart).TotalSeconds;
    }

    private static DateTime ParseUtc(string value) => DateTime.Parse(value, null,
        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}
