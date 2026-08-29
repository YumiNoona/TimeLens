using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;
using TimeLens.TrayApp;
using TimeLens.TrayApp.Services;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var directory = Path.Combine(Path.GetTempPath(), "TimeLens-startup-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Run("New user can initialize and load settings", () =>
            {
                var path = Path.Combine(directory, "new.db");
                var settings = DatabaseInitializer.Initialize(path);
                Check(settings.RetentionDays == 90 && settings.TrackInput, "Fresh defaults were not loaded.");
                Check(Query(path, "SELECT count(*) FROM custom_rules") == 0, "Fresh rules table missing.");
            });
            Run("Empty database from a failed first launch recovers", () =>
            {
                var path = Path.Combine(directory, "empty.db");
                // Reproduce the old startup ordering on a new user profile.
                try
                {
                    new SettingsService(path).Load();
                    throw new Exception("Expected the original missing-settings-table failure.");
                }
                catch (SqliteException ex) when (ex.SqliteErrorCode == 1 && ex.Message.Contains("no such table: settings")) { }
                DatabaseInitializer.Initialize(path);
                Check(new SettingsService(path).Load().Theme == "default", "Empty database did not recover.");
            });
            Run("Restart preserves settings and applies saved retention", () =>
            {
                var path = Path.Combine(directory, "existing.db");
                DatabaseInitializer.Initialize(path);
                var service = new SettingsService(path);
                service.Save("retention_days", "365");
                service.Save("theme", "dark");
                service.Save("first_run_done", "true");
                using (var conn = new SqliteConnection($"Data Source={path}"))
                {
                    conn.Open();
                    using var insert = conn.CreateCommand();
                    insert.CommandText = "INSERT INTO session_events(event_type,timestamp) VALUES ('wake',$keep),('sleep',$purge)";
                    insert.Parameters.AddWithValue("$keep", DateTime.UtcNow.AddDays(-180).ToString("o"));
                    insert.Parameters.AddWithValue("$purge", DateTime.UtcNow.AddDays(-400).ToString("o"));
                    insert.ExecuteNonQuery();
                }
                var settings = DatabaseInitializer.Initialize(path);
                Check(settings.RetentionDays == 365 && settings.Theme == "dark", "Saved preferences changed.");
                Check(Query(path, "SELECT count(*) FROM session_events WHERE event_type='wake'") == 1, "Saved retention was ignored; history was deleted.");
                Check(Query(path, "SELECT count(*) FROM session_events WHERE event_type='sleep'") == 0, "Expired history was not purged.");
                Check(Query(path, "SELECT count(*) FROM settings WHERE key='first_run_done' AND value='true'") == 1, "Onboarding reset.");
            });
            Run("Legacy rules migrate before the priority index is created", () =>
            {
                var path = Path.Combine(directory, "legacy.db");
                using (var conn = new SqliteConnection($"Data Source={path}"))
                {
                    conn.Open();
                    using var seed = conn.CreateCommand();
                    seed.CommandText = "CREATE TABLE custom_rules(exe_pattern TEXT PRIMARY KEY, category TEXT NOT NULL); INSERT INTO custom_rules VALUES ('editor','Development')";
                    seed.ExecuteNonQuery();
                }
                DatabaseInitializer.Initialize(path);
                Check(Query(path, "SELECT count(*) FROM custom_rules WHERE id > 0 AND exe_pattern='editor' AND category='Development' AND priority=0 AND rule_type='substring'") == 1, "Legacy custom rule lost.");
                DatabaseInitializer.Initialize(path);
                Check(Query(path, "SELECT count(*) FROM custom_rules") == 1, "Restart duplicated legacy rules.");
            });
            Run("Corrupt database reports failure instead of resetting data", () =>
            {
                var path = Path.Combine(directory, "corrupt.db");
                File.WriteAllText(path, "not a sqlite database");
                try
                {
                    DatabaseInitializer.Initialize(path);
                    throw new Exception("Corrupt database unexpectedly initialized.");
                }
                catch (SqliteException) { }
                SqliteConnection.ClearAllPools();
                Check(File.ReadAllText(path) == "not a sqlite database", "Corrupt data was overwritten.");
            });
            Run("Per-user Windows startup registration is quoted, verified, and removable", () =>
            {
                var testKey = $@"Software\TimeLens\StartupTests\{Guid.NewGuid():N}\Run";
                const string valueName = "TimeLens-Test";
                var executablePath = Path.Combine(directory, "Program Files", "TimeLens.exe");
                try
                {
                    Check(!AutoStartManager.IsAutoStartEnabled(Microsoft.Win32.Registry.CurrentUser, testKey, valueName, executablePath), "Missing startup entry reported enabled.");
                    AutoStartManager.SetAutoStart(Microsoft.Win32.Registry.CurrentUser, testKey, valueName, executablePath, true);
                    using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(testKey))
                        Check(string.Equals(key?.GetValue(valueName) as string, $"\"{Path.GetFullPath(executablePath)}\" --startup", StringComparison.Ordinal), "Startup command was not safely quoted.");
                    Check(AutoStartManager.IsAutoStartEnabled(Microsoft.Win32.Registry.CurrentUser, testKey, valueName, executablePath), "Valid startup entry was not detected.");
                    Check(!AutoStartManager.IsAutoStartEnabled(Microsoft.Win32.Registry.CurrentUser, testKey, valueName, Path.Combine(directory, "moved.exe")), "Stale executable path reported enabled.");
                    AutoStartManager.SetAutoStart(Microsoft.Win32.Registry.CurrentUser, testKey, valueName, executablePath, false);
                    Check(!AutoStartManager.IsAutoStartEnabled(Microsoft.Win32.Registry.CurrentUser, testKey, valueName, executablePath), "Startup entry was not removed.");
                }
                finally
                {
                    Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(testKey[..testKey.LastIndexOf("\\Run", StringComparison.Ordinal)], throwOnMissingSubKey: false);
                }
            });
            if (args.Contains("--tray"))
                Run("Native tray registration, activation, and Explorer recovery", TestTray);
            Console.WriteLine("All startup regression checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void TestTray()
    {
        using var tray = new NativeTrayIcon();
        var opened = 0;
        tray.OpenDashboardRequested += () => opened++;
        tray.StartupRequested += () =>
        {
            var hwnd = FindWindowW("TimeLensHiddenWindow", "TimeLens");
            Check(hwnd != IntPtr.Zero && GetParent(hwnd) == IntPtr.Zero, "Tray is not a top-level broadcast recipient.");
            var identifier = new IconIdentifier { Size = (uint)Marshal.SizeOf<IconIdentifier>(), Window = hwnd, Id = 100 };
            Check(Shell_NotifyIconGetRect(ref identifier, out _) == 0, "Shell did not register the tray icon.");
            // Simulate the version-4 keyboard activation delivered by the shell.
            SendMessageW(hwnd, 0x400, IntPtr.Zero, new IntPtr((100 << 16) | 0x401));
            Check(opened == 1, "Keyboard activation did not open the dashboard.");
            var icon = new IconData { Size = 976, Window = hwnd, Id = 100 };
            Check(Shell_NotifyIconW(2, ref icon), "Could not remove the test icon to simulate Explorer restarting.");
            Check(Shell_NotifyIconGetRect(ref identifier, out _) != 0, "Test icon was not removed.");
            SendMessageW(hwnd, RegisterWindowMessageW("TaskbarCreated"), IntPtr.Zero, IntPtr.Zero);
            Check(Shell_NotifyIconGetRect(ref identifier, out _) == 0, "Tray icon was lost after recovery notification.");
            tray.Close();
        };
        tray.Run();
    }

    private static long Query(string path, string sql)
    {
        using var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Run(string name, Action action)
    {
        action();
        Console.WriteLine($"PASS: {name}");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconIdentifier { public uint Size; public IntPtr Window; public uint Id; public Guid Guid; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    // NOTIFYICONDATAW is 976 bytes in our win-x64 target. Only deletion fields are needed.
    [StructLayout(LayoutKind.Sequential, Size = 976)]
    private struct IconData { public uint Size; public IntPtr Window; public uint Id; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIconW(uint command, ref IconData data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowW(string className, string title);
    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr hwnd);
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string message);
    [DllImport("shell32.dll")]
    private static extern int Shell_NotifyIconGetRect(ref IconIdentifier identifier, out Rect rect);
}
