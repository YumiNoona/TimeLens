using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;
using TimeLens.Api;
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
                Check(settings.RetentionDays == 90 && settings.TrackInput && settings.HeatmapDays == 273, "Fresh defaults were not loaded.");
                Check(settings.BlockTitle == BlockNotification.DefaultTitle &&
                      settings.BlockMessage == BlockNotification.DefaultMessage &&
                      settings.BlockImageVersion == "" && settings.BlockMediaType == "" &&
                      settings.BlockNotifyIntervalSeconds == 300 && settings.BlockNotifyPosition == "bottom-left" &&
                      settings.BlockMediaLayout == "large",
                      "Fresh block reminder defaults were not loaded.");
                Check(Query(path, "SELECT count(*) FROM custom_rules") == 0, "Fresh rules table missing.");
            });
            Run("Block modes have distinct enforcement plans", () =>
            {
                var notify = BlockActionPlan.From("notify");
                var hide = BlockActionPlan.From("hide");
                var kill = BlockActionPlan.From("kill");
                var strict = BlockActionPlan.From("strict");
                Check(notify.ShowNotification && !notify.MinimizeWindows && !notify.TerminateProcesses && !notify.RepeatEveryFiveSeconds, "Notify plan enforces the target.");
                Check(hide.MinimizeWindows && !hide.TerminateProcesses && !hide.RepeatEveryFiveSeconds, "Hide plan is incorrect.");
                Check(!kill.MinimizeWindows && kill.TerminateProcesses && !kill.RepeatEveryFiveSeconds, "Kill plan is incorrect.");
                Check(strict.MinimizeWindows && strict.TerminateProcesses && strict.RepeatEveryFiveSeconds, "Strict plan is incomplete.");
                Check(BlockActionPlan.From("invalid").Id == "hide", "Invalid actions do not fall back safely.");

                var calls = new List<string>();
                bool Minimize() { calls.Add("minimize"); return true; }
                bool Terminate() { calls.Add("terminate"); return true; }
                Check(BlockEnforcement.Apply(notify, true, Minimize, Terminate) && calls.Count == 0, "Notify invoked enforcement.");
                calls.Clear();
                Check(BlockEnforcement.Apply(hide, true, Minimize, Terminate) && calls.SequenceEqual(["minimize"]), "Hide did not only minimize.");
                calls.Clear();
                Check(BlockEnforcement.Apply(kill, true, Minimize, Terminate) && calls.SequenceEqual(["terminate"]), "Kill did not only terminate.");
                calls.Clear();
                Check(BlockEnforcement.Apply(strict, true, Minimize, Terminate) && calls.SequenceEqual(["minimize", "terminate"]), "Strict enforcement order is incorrect.");
            });
            Run("Custom block reminder placeholders are formatted safely", () =>
            {
                var message = BlockNotification.Format("Close {TARGET}; mode={mode}", "games.exe", "strict");
                Check(message == "Close games.exe; mode=strict", "Block reminder placeholders were not replaced.");
                Check(BlockNotification.FormatTitle("Stop {target}", "games.exe", "hide") == "Stop games.exe", "Block title placeholder was not replaced.");
                Check(BlockNotification.NormalizeTitle("  Deep   work\r\nnow  ") == "Deep work now", "Block title whitespace was not normalized.");
                Check(BlockNotification.NormalizeMessage("") == BlockNotification.DefaultMessage, "Empty block message did not restore the default.");
            });
            Run("Per-target app and website actions remain distinct", () =>
            {
                Check(BlockTargetAction.Resolve("discord.exe", "kill", "hide") == "kill", "Explicit app action did not override the legacy default.");
                Check(BlockTargetAction.Resolve("youtube.com", "notify", "strict") == "notify", "Website Notify was not preserved.");
                Check(BlockTargetAction.Normalize("youtube.com", "hide") is null, "A desktop-only mode was accepted for a website.");
                Check(BlockTargetAction.Resolve("reddit.com", "hide", "hide") == "strict", "Legacy desktop actions were not safely mapped to website Strict.");
                Check(BlockTargetAction.Resolve("editor.exe", null, "hide") == "hide", "Legacy app action was not retained.");
                Check(BlockTargetAction.Resolve("editor.exe", "notify", "strict") == "hide", "Legacy app Notify was not migrated to Hide.");
                Check(BlockTargetAction.Resolve("explorer.exe", "notify", "strict") == "hide", "Explorer Notify was not migrated to Hide.");
                Check(!BlockTargetAction.IsUnsafeShellAction("explorer.exe", "hide", "notify"), "Explorer Hide was rejected.");
                Check(BlockTargetAction.IsUnsafeShellAction("explorer.exe", "kill", "hide"), "Explorer Kill was accepted.");
                Check(BlockTargetAction.IsUnsafeShellAction("explorer.exe", "strict", "hide"), "Explorer Strict was accepted.");
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
                service.Save("block_notify_interval_seconds", "1800");
                service.Save("block_notify_position", "top-right");
                service.Save("block_media_layout", "banner");
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
                Check(settings.RetentionDays == 365 && settings.Theme == "dark" &&
                      settings.BlockNotifyIntervalSeconds == 1800 && settings.BlockNotifyPosition == "top-right" &&
                      settings.BlockMediaLayout == "banner",
                      "Saved preferences changed.");
                Check(Query(path, "SELECT count(*) FROM session_events WHERE event_type='wake'") == 1, "Saved retention was ignored; history was deleted.");
                Check(Query(path, "SELECT count(*) FROM session_events WHERE event_type='sleep'") == 0, "Expired history was not purged.");
                Check(Query(path, "SELECT count(*) FROM settings WHERE key='first_run_done' AND value='true'") == 1, "Onboarding reset.");
            });
            Run("Restart closes orphaned browser activity and retains dashboard indexes", () =>
            {
                var path = Path.Combine(directory, "browser-restart.db");
                DatabaseInitializer.Initialize(path);
                using (var conn = new SqliteConnection($"Data Source={path}"))
                {
                    conn.Open();
                    using var insert = conn.CreateCommand();
                    insert.CommandText = "INSERT INTO browser_events(domain, start_time, browser, local_date) VALUES ('example.com', $start, 'chrome', $date)";
                    insert.Parameters.AddWithValue("$start", DateTime.UtcNow.AddMinutes(-2).ToString("o"));
                    insert.Parameters.AddWithValue("$date", DateTime.Now.ToString("yyyy-MM-dd"));
                    insert.ExecuteNonQuery();
                }
                DatabaseInitializer.Initialize(path);
                Check(Query(path, "SELECT count(*) FROM browser_events WHERE end_time IS NOT NULL") == 1, "An open browser row survived restart.");
                Check(Query(path, "SELECT count(*) FROM sqlite_master WHERE type='index' AND name='idx_app_local_date_state'") == 1, "App dashboard index was not created.");
                Check(Query(path, "SELECT count(*) FROM sqlite_master WHERE type='index' AND name='idx_browser_local_date'") == 1, "Browser dashboard index was not created.");
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
            if (args.Contains("--toast"))
                Run("Native toast window creation and rendering", () =>
                {
                    using var toast = new ToastWindow("Focus Mode", "example.exe is blocked");
                    var toastWindow = FindWindowW("TLToast", "");
                    Check(toastWindow != IntPtr.Zero, "Native toast window was not created.");
                    Check(IsWindowVisible(toastWindow), "Native toast window was created hidden.");
                    SendMessageW(toastWindow, 0x0202, IntPtr.Zero, new IntPtr((70 << 16) | 220));
                    Check(IsWindowVisible(toastWindow), "Clicking the toast body dismissed a persistent reminder.");
                    SendMessageW(toastWindow, 0x0202, IntPtr.Zero, new IntPtr((25 << 16) | 430));
                    Check(!IsWindow(toastWindow), "The toast close button did not dismiss the reminder.");
                });
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
            Action failingLaunch = () => throw new IOException("Injected browser launch failure");
            tray.OpenDashboardRequested += failingLaunch;
            SendMessageW(hwnd, 0x400, IntPtr.Zero, new IntPtr((100 << 16) | 0x401));
            tray.OpenDashboardRequested -= failingLaunch;
            SendMessageW(hwnd, 0x400, IntPtr.Zero, new IntPtr((100 << 16) | 0x401));
            Check(opened == 3 && IsWindow(hwnd), "A failed dashboard launch stopped the native tray loop.");
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
    private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string message);
    [DllImport("shell32.dll")]
    private static extern int Shell_NotifyIconGetRect(ref IconIdentifier identifier, out Rect rect);
}
