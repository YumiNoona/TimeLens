using Microsoft.Data.Sqlite;

namespace TimeLens.TrayApp.Services;

public static class DatabaseInitializer
{
    public static void Initialize(string dbPath)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode = WAL;

            CREATE TABLE IF NOT EXISTS app_events (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                exe_name TEXT NOT NULL,
                window_title TEXT,
                pid INTEGER,
                category TEXT,
                start_time TEXT NOT NULL,
                end_time TEXT,
                was_idle INTEGER NOT NULL DEFAULT 0,
                session_state TEXT NOT NULL DEFAULT 'active',
                idle_reason TEXT
            );

            CREATE TABLE IF NOT EXISTS browser_events (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                domain TEXT NOT NULL,
                url TEXT,
                title TEXT,
                category TEXT,
                start_time TEXT NOT NULL,
                end_time TEXT,
                browser TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS session_events (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                event_type TEXT NOT NULL,
                timestamp TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS input_activity (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp TEXT NOT NULL,
                keystroke_count INTEGER NOT NULL DEFAULT 0,
                click_count INTEGER NOT NULL DEFAULT 0,
                pid INTEGER,
                exe_name TEXT
            );

            CREATE TABLE IF NOT EXISTS audio_activity (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp TEXT NOT NULL,
                pid INTEGER,
                exe_name TEXT,
                session_id INTEGER,
                is_playing INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS app_categories (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                exe_name TEXT NOT NULL UNIQUE,
                category TEXT NOT NULL,
                is_user_defined INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            INSERT OR IGNORE INTO settings (key, value) VALUES ('track_audio', 'true');
            INSERT OR IGNORE INTO settings (key, value) VALUES ('track_browser', 'true');
            INSERT OR IGNORE INTO settings (key, value) VALUES ('track_input', 'true');
            INSERT OR IGNORE INTO settings (key, value) VALUES ('idle_threshold_seconds', '180');

            CREATE INDEX IF NOT EXISTS idx_app_start ON app_events(start_time);
            CREATE INDEX IF NOT EXISTS idx_browser_start ON browser_events(start_time);
            """;
        cmd.ExecuteNonQuery();

        // Migrate existing databases — add new columns if missing
        MigrateAddColumn(conn, "app_events", "session_state", "TEXT NOT NULL DEFAULT 'active'");
        MigrateAddColumn(conn, "app_events", "idle_reason", "TEXT");

        // Backfill session_state for rows that still have NULL
        using var backfill = conn.CreateCommand();
        backfill.CommandText = """
            UPDATE app_events SET session_state = 'idle' WHERE session_state IS NULL AND was_idle = 1;
            """;
        backfill.ExecuteNonQuery();
        using var backfill2 = conn.CreateCommand();
        backfill2.CommandText = """
            UPDATE app_events SET session_state = 'active' WHERE session_state IS NULL;
            """;
        backfill2.ExecuteNonQuery();

        // Retention: purge rows older than 90 days
        var cutoff = DateTime.UtcNow.AddDays(-90).ToString("o");
        using var purge = conn.CreateCommand();
        purge.CommandText = $"""
            DELETE FROM app_events WHERE start_time < $cutoff;
            DELETE FROM browser_events WHERE start_time < $cutoff;
            DELETE FROM session_events WHERE timestamp < $cutoff;
            DELETE FROM input_activity WHERE timestamp < $cutoff;
            DELETE FROM audio_activity WHERE timestamp < $cutoff;
            """;
        purge.Parameters.AddWithValue("$cutoff", cutoff);
        var deleted = purge.ExecuteNonQuery();

        // Enable incremental auto_vacuum so free pages are reused
        using var av = conn.CreateCommand();
        av.CommandText = "PRAGMA auto_vacuum = INCREMENTAL;";
        av.ExecuteNonQuery();

        // Vacuum if we deleted anything meaningful
        if (deleted > 100)
        {
            using var v1 = conn.CreateCommand();
            v1.CommandText = "PRAGMA incremental_vacuum;";
            v1.ExecuteNonQuery();
        }
    }

    private static void MigrateAddColumn(SqliteConnection conn, string table, string column, string def)
    {
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {def};";
            cmd.ExecuteNonQuery();
        }
        catch
        {
            // Column already exists — ignore
        }
    }
}
