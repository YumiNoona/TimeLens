using Microsoft.Data.Sqlite;

namespace TimeLens.TrayApp.Services;

public static class DatabaseInitializer
{
    public static void Initialize(string dbPath, int retentionDays = 90)
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
                tab_id INTEGER,
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

            CREATE TABLE IF NOT EXISTS custom_rules (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                exe_pattern TEXT NOT NULL,
                category TEXT NOT NULL,
                rule_type TEXT NOT NULL DEFAULT 'substring',
                target TEXT NOT NULL DEFAULT 'exe',
                priority INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS idx_custom_rules_priority ON custom_rules(priority);

            CREATE TABLE IF NOT EXISTS settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS block_log (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                blocked_exe TEXT NOT NULL,
                blocked_action TEXT NOT NULL,
                timestamp TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS idle_spans (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                start_time TEXT NOT NULL,
                end_time TEXT,
                exe_at_start TEXT,
                idle_reason TEXT
            );

            CREATE TABLE IF NOT EXISTS goals (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                goal_type TEXT NOT NULL,
                target TEXT NOT NULL,
                threshold_minutes INTEGER NOT NULL,
                notify_at INTEGER DEFAULT 80,
                enabled INTEGER DEFAULT 1,
                last_notified TEXT
            );

            INSERT OR IGNORE INTO settings (key, value) VALUES ('track_audio', 'true');
            INSERT OR IGNORE INTO settings (key, value) VALUES ('track_browser', 'true');
            INSERT OR IGNORE INTO settings (key, value) VALUES ('track_input', 'true');
            INSERT OR IGNORE INTO settings (key, value) VALUES ('idle_threshold_seconds', '180');
            INSERT OR IGNORE INTO settings (key, value) VALUES ('theme', 'default');
            INSERT OR IGNORE INTO settings (key, value) VALUES ('timeline_grouped', 'true');
            INSERT OR IGNORE INTO settings (key, value) VALUES ('time_format', '12h');
            INSERT OR IGNORE INTO settings (key, value) VALUES ('poll_interval_seconds', '30');

            CREATE INDEX IF NOT EXISTS idx_app_start ON app_events(start_time);
            CREATE INDEX IF NOT EXISTS idx_browser_start ON browser_events(start_time);
            CREATE INDEX IF NOT EXISTS idx_input_activity_ts ON input_activity(timestamp);
            CREATE INDEX IF NOT EXISTS idx_audio_activity_ts ON audio_activity(timestamp);
            CREATE INDEX IF NOT EXISTS idx_session_events_ts ON session_events(timestamp);
            """;
        cmd.ExecuteNonQuery();

        // Migrate existing databases — add new columns if missing
        MigrateAddColumn(conn, "app_events", "session_state", "TEXT NOT NULL DEFAULT 'active'");
        MigrateAddColumn(conn, "app_events", "idle_reason", "TEXT");
        MigrateAddColumn(conn, "app_events", "local_date", "TEXT");
        MigrateAddColumn(conn, "app_events", "project", "TEXT");
        MigrateAddColumn(conn, "browser_events", "tab_id", "INTEGER");
        MigrateAddColumn(conn, "custom_rules", "rule_type", "TEXT NOT NULL DEFAULT 'substring'");
        MigrateAddColumn(conn, "custom_rules", "target", "TEXT NOT NULL DEFAULT 'exe'");
        MigrateAddColumn(conn, "custom_rules", "priority", "INTEGER NOT NULL DEFAULT 0");
        // Migrate old primary-key-based rows to new auto-increment schema
        using var migRules = conn.CreateCommand();
        migRules.CommandText = "UPDATE custom_rules SET rule_type='substring', target='exe' WHERE rule_type IS NULL";
        migRules.ExecuteNonQuery();

        // Rebuild custom_rules if missing the auto-increment id column (old schema)
        try
        {
            using var checkId = conn.CreateCommand();
            checkId.CommandText = "SELECT id FROM custom_rules LIMIT 1";
            checkId.ExecuteNonQuery();
        }
        catch
        {
            using var rebuild = conn.CreateCommand();
            rebuild.CommandText = """
                CREATE TABLE custom_rules_new (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    exe_pattern TEXT NOT NULL,
                    category TEXT NOT NULL,
                    rule_type TEXT NOT NULL DEFAULT 'substring',
                    target TEXT NOT NULL DEFAULT 'exe',
                    priority INTEGER NOT NULL DEFAULT 0
                );
                INSERT INTO custom_rules_new (exe_pattern, category, rule_type, target, priority)
                    SELECT exe_pattern, category,
                           COALESCE(rule_type, 'substring'),
                           COALESCE(target, 'exe'),
                           COALESCE(priority, 0)
                    FROM custom_rules;
                DROP TABLE custom_rules;
                ALTER TABLE custom_rules_new RENAME TO custom_rules;
                CREATE INDEX IF NOT EXISTS idx_custom_rules_priority ON custom_rules(priority);
                """;
            rebuild.ExecuteNonQuery();
        }

        // Fix any broken rows where end_time < start_time (timezone/timer bug)
        using var fixNeg = conn.CreateCommand();
        fixNeg.CommandText = """
            UPDATE app_events SET end_time = start_time
            WHERE end_time IS NOT NULL AND end_time < start_time
            """;
        fixNeg.ExecuteNonQuery();

        // Patch orphaned rows from previous sessions (race left end_time=NULL)
        // Use the next event's start_time as end boundary for best accuracy;
        // fall back to start_time + 30 minutes if no next event exists.
        // NOTE: All comparisons use ISO 8601 strings to match stored format (e.g. "2026-06-25T10:36:15.6682266Z")
        //       SQLite's datetime() can't parse ISO 8601 directly, so REPLACE T→space and strip Z first.
        var orphanCutoff = DateTime.UtcNow.AddMinutes(-10).ToString("o");
        using var patchOrphans = conn.CreateCommand();
        patchOrphans.CommandText = """
            UPDATE app_events SET end_time = COALESCE(
                (SELECT MIN(next.start_time) FROM app_events next
                 WHERE next.start_time > app_events.start_time
                   AND next.start_time < $fortyeight),
                datetime(REPLACE(REPLACE(start_time, 'T', ' '), 'Z', ''), '+30 minutes')
            )
            WHERE end_time IS NULL AND start_time < $cutoff
            """;
        patchOrphans.Parameters.AddWithValue("$cutoff", orphanCutoff);
        patchOrphans.Parameters.AddWithValue("$fortyeight", DateTime.UtcNow.AddHours(48).ToString("o"));
        patchOrphans.ExecuteNonQuery();

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
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays).ToString("o");
        using var purge = conn.CreateCommand();
        purge.CommandText = $"""
            DELETE FROM app_events WHERE start_time < $cutoff;
            DELETE FROM browser_events WHERE start_time < $cutoff;
            DELETE FROM idle_spans WHERE start_time < $cutoff;
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
