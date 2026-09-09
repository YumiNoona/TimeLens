using Microsoft.Data.Sqlite;

namespace TimeLens.TrayApp.Services;

public static class DataRetentionService
{
    public static int Purge(string dbPath, int retentionDays)
    {
        if (retentionDays is < 1 or > 3650) retentionDays = 90;
        var cutoffUtc = DateTime.UtcNow.AddDays(-retentionDays);
        var cutoff = cutoffUtc.ToString("o");
        var localDate = cutoffUtc.ToLocalTime().ToString("yyyy-MM-dd");

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            DELETE FROM app_events
              WHERE julianday(COALESCE(end_time, start_time)) <= julianday($cutoff);
            UPDATE app_events SET start_time=$cutoff, local_date=$localDate
              WHERE julianday(start_time) < julianday($cutoff)
                AND julianday(COALESCE(end_time, start_time)) > julianday($cutoff);

            DELETE FROM browser_events
              WHERE julianday(COALESCE(end_time, start_time)) <= julianday($cutoff);
            UPDATE browser_events SET start_time=$cutoff, local_date=$localDate
              WHERE julianday(start_time) < julianday($cutoff)
                AND julianday(COALESCE(end_time, start_time)) > julianday($cutoff);

            DELETE FROM idle_spans
              WHERE julianday(COALESCE(end_time, start_time)) <= julianday($cutoff);
            UPDATE idle_spans SET start_time=$cutoff
              WHERE julianday(start_time) < julianday($cutoff)
                AND julianday(COALESCE(end_time, start_time)) > julianday($cutoff);

            DELETE FROM browser_input_batches WHERE julianday(timestamp) < julianday($cutoff);
            DELETE FROM session_events WHERE julianday(timestamp) < julianday($cutoff);
            DELETE FROM input_activity WHERE julianday(timestamp) < julianday($cutoff);
            DELETE FROM audio_activity WHERE julianday(timestamp) < julianday($cutoff);
            DELETE FROM block_log WHERE julianday(timestamp) < julianday($cutoff);
            """;
        cmd.Parameters.AddWithValue("$cutoff", cutoff);
        cmd.Parameters.AddWithValue("$localDate", localDate);
        var changed = cmd.ExecuteNonQuery();
        tx.Commit();

        using var optimize = conn.CreateCommand();
        optimize.CommandText = "PRAGMA incremental_vacuum(256); PRAGMA optimize;";
        optimize.ExecuteNonQuery();
        return changed;
    }
}
