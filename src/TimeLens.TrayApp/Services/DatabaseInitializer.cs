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
                was_idle INTEGER NOT NULL DEFAULT 0
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

            CREATE INDEX IF NOT EXISTS idx_app_start ON app_events(start_time);
            CREATE INDEX IF NOT EXISTS idx_browser_start ON browser_events(start_time);
            """;
        cmd.ExecuteNonQuery();
    }
}
