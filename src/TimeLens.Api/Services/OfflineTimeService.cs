using Microsoft.Data.Sqlite;

namespace TimeLens.Api.Services;

public sealed record OfflineEntry(long Id, string StartTime, string EndTime, string Kind, string Project, string Notes);
public sealed record IdleReview(long Id, string StartTime, string EndTime);
public sealed record OfflineRequest(long? IdleSpanId, string? StartTime, string? EndTime, string Kind, string Project = "", string Notes = "");

/// <summary>User-confirmed offline work; never added to the automatic foreground clock.</summary>
public sealed class OfflineTimeService(string dbPath)
{
    private SqliteConnection Open() { var conn = new SqliteConnection($"Data Source={dbPath}"); conn.Open(); return conn; }

    public IdleReview[] Pending()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id,start_time,end_time FROM idle_spans WHERE reviewed=0 AND end_time IS NOT NULL ORDER BY id DESC LIMIT 20";
        using var rows = cmd.ExecuteReader();
        var items = new List<IdleReview>();
        while (rows.Read()) items.Add(new(rows.GetInt64(0), rows.GetString(1), rows.GetString(2)));
        return items.ToArray();
    }

    public OfflineEntry[] Read(DateTime localDate)
    {
        var start = DateTime.SpecifyKind(localDate.Date, DateTimeKind.Local).ToUniversalTime();
        var end = DateTime.SpecifyKind(localDate.Date.AddDays(1), DateTimeKind.Local).ToUniversalTime();
        return ReadRange(start, end);
    }

    public OfflineEntry[] ReadRange(DateTime start, DateTime end)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id,start_time,end_time,kind,project,notes FROM offline_entries WHERE start_time < $end AND end_time > $start ORDER BY start_time";
        cmd.Parameters.AddWithValue("$start", start.ToString("o"));
        cmd.Parameters.AddWithValue("$end", end.ToString("o"));
        using var rows = cmd.ExecuteReader();
        var items = new List<OfflineEntry>();
        while (rows.Read())
        {
            var entryStart = DateTimeOffset.Parse(rows.GetString(1)).UtcDateTime;
            var entryEnd = DateTimeOffset.Parse(rows.GetString(2)).UtcDateTime;
            items.Add(new(rows.GetInt64(0), (entryStart < start ? start : entryStart).ToString("o"),
                (entryEnd > end ? end : entryEnd).ToString("o"), rows.GetString(3), rows.GetString(4), rows.GetString(5)));
        }
        return items.ToArray();
    }

    public void Save(OfflineRequest request)
    {
        if (request.Kind is not ("meeting" or "call" or "brainstorming" or "work" or "break" or "personal"))
            throw new ArgumentException("Choose a valid offline activity.");
        if ((request.Project?.Length ?? 0) > 120 || (request.Notes?.Length ?? 0) > 500)
            throw new ArgumentException("Project is limited to 120 characters and notes to 500.");
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        string? startText = request.StartTime, endText = request.EndTime;
        if (request.IdleSpanId is { } idle)
        {
            using var span = conn.CreateCommand();
            span.CommandText = "SELECT start_time,end_time,reviewed FROM idle_spans WHERE id=$id";
            span.Parameters.AddWithValue("$id", idle);
            using var row = span.ExecuteReader();
            if (!row.Read() || row.IsDBNull(1)) throw new ArgumentException("This idle interval no longer exists.");
            if (row.GetInt32(2) != 0) { tx.Commit(); return; } // Idempotent reconciliation.
            startText = row.GetString(0); endText = row.GetString(1);
        }
        if (!DateTimeOffset.TryParse(startText, out var start) || !DateTimeOffset.TryParse(endText, out var end) ||
            end <= start || end > DateTimeOffset.UtcNow.AddSeconds(5) ||
            (end - start > TimeSpan.FromDays(1) && request.Kind is not ("break" or "personal")) ||
            start < DateTimeOffset.UtcNow.AddDays(-LiveStatusStore.Settings.RetentionDays))
            throw new ArgumentException("Use a past interval of at most 24 hours within your retention period.");
        using var overlap = conn.CreateCommand();
        overlap.CommandText = """
            SELECT EXISTS(SELECT 1 FROM offline_entries WHERE start_time < $end AND end_time > $start)
                OR EXISTS(SELECT 1 FROM app_events WHERE session_state='active'
                    AND julianday(start_time) < julianday($end) AND julianday(end_time) > julianday($start))
            """;
        overlap.Parameters.AddWithValue("$start", start.UtcDateTime.ToString("o"));
        overlap.Parameters.AddWithValue("$end", end.UtcDateTime.ToString("o"));
        if (request.Kind is not ("break" or "personal") && Convert.ToInt64(overlap.ExecuteScalar()) != 0)
            throw new ArgumentException("This interval overlaps recorded active or offline time. Choose an untracked interval.");
        if (request.Kind is not ("break" or "personal"))
        {
            using var insert = conn.CreateCommand();
            insert.CommandText = "INSERT INTO offline_entries(idle_span_id,start_time,end_time,kind,project,notes) VALUES($idle,$start,$end,$kind,$project,$notes)";
            insert.Parameters.AddWithValue("$idle", request.IdleSpanId ?? (object)DBNull.Value);
            insert.Parameters.AddWithValue("$start", start.UtcDateTime.ToString("o"));
            insert.Parameters.AddWithValue("$end", end.UtcDateTime.ToString("o"));
            insert.Parameters.AddWithValue("$kind", request.Kind);
            insert.Parameters.AddWithValue("$project", PrivacyPolicy.Title(request.Project ?? ""));
            insert.Parameters.AddWithValue("$notes", PrivacyPolicy.Title(request.Notes ?? ""));
            insert.ExecuteNonQuery();
        }
        if (request.IdleSpanId is { } id)
        {
            using var review = conn.CreateCommand();
            review.CommandText = "UPDATE idle_spans SET reviewed=1,idle_reason=$kind WHERE id=$id";
            review.Parameters.AddWithValue("$id", id);
            review.Parameters.AddWithValue("$kind", request.Kind);
            review.ExecuteNonQuery();
        }
        tx.Commit();
        LiveStatusStore.PendingIdleReturn = false;
    }

    public void Delete(long id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM offline_entries WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }
}
