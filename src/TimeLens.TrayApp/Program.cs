using System.Runtime.InteropServices;
using TimeLens.Api;
using TimeLens.TrayApp.Services;
using TimeLens.TrayApp.Watchers;

namespace TimeLens.TrayApp;

internal sealed record BlockEntry(string I, string M, string? E)
{
    public bool IsExpired() => M == "t" && E is not null &&
        DateTime.TryParse(E, null, System.Globalization.DateTimeStyles.RoundtripKind, out var exp) &&
        DateTime.UtcNow >= exp;
}

internal static class Program
{
    private const string MutexName = "TimeLens-TrayApp-Instance";
    private const int MB_YESNO = 0x04;
    private const int MB_ICONQUESTION = 0x20;
    private const int IDYES = 6;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    [STAThread]
    private static void Main()
    {
        try
        {
            // Pre-load native SQLite library from runtime subfolder before any DB code runs
            NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "runtime", "e_sqlite3.dll"));

            MainImpl();
        }
        catch (Exception ex)
        {
            System.IO.File.AppendAllText(
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "TimeLens", "crash.log"),
                $"{DateTime.UtcNow:o} Fatal: {ex}{Environment.NewLine}");
            Environment.Exit(1);
        }
    }

    private static void MainImpl()
    {
        Mutex? mutex = null;
        try
        {
            mutex = Mutex.OpenExisting(MutexName);
            mutex.Dispose();
            return; // Another instance is already running
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            mutex = new Mutex(true, MutexName, out _);
        }

        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TimeLens", "activity.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        var settingsSvc = new SettingsService(dbPath);
        var settings = settingsSvc.Load();
        RuntimeConfig.Settings = settings;
        LiveStatusStore.Settings = settings;

        DatabaseInitializer.Initialize(dbPath, settings.RetentionDays);

        var writer = new EventWriter(dbPath);
        var classifier = new CategoryClassifier();

        // Load community built-in rules first (lowest priority, overridden by user rules)
        var userCsvPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TimeLens", "categories.csv");
        var builtinCsvPath = Path.Combine(AppContext.BaseDirectory, "runtime", "categories.csv");
        var csvPath = File.Exists(userCsvPath) ? userCsvPath : builtinCsvPath;
        classifier.LoadBuiltins(csvPath);

        // Load user custom rules from DB — these override builtins (priority 0 < 100)
        using (var loadConn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
        {
            loadConn.Open();
            using var loadCmd = loadConn.CreateCommand();
            loadCmd.CommandText = "SELECT exe_pattern, category, COALESCE(rule_type,'substring'), COALESCE(target,'exe'), COALESCE(priority,0) FROM custom_rules";
            using var reader = loadCmd.ExecuteReader();
            while (reader.Read())
                classifier.AddCustomRule(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4));
        }
        var winWatcher = new WinEventWatcher();
        var idleMonitor = new IdleMonitor { IdleThresholdSeconds = settings.IdleThresholdSeconds };
        var sessionWatcher = new SessionWatcher();
        var inputMonitor = new InputMonitor();
        var audioMonitor = new AudioMonitor();

        if (settings.TrackAudio)
            idleMonitor.AudioMonitorRef = audioMonitor;

        void WriteAppEvent()
        {
            var (exe, title, pid) = Win32.GetForegroundWindowInfo();
            if (ShouldSkipBrowserAppRow(exe)) return;
            var cat = classifier.Classify(exe, title);
            var state = idleMonitor.GetState();
            var project = CategoryClassifier.ExtractProject(exe, title);
            writer.OpenAppEvent(exe, title, pid, state, cat, project);
            LiveStatusStore.CurrentApp = exe;
            LiveStatusStore.IsIdle = state != "active";
            LiveStatusStore.IdleSeconds = idleMonitor.IdleSeconds();
            LiveStatusStore.SystemState = state;
        }

        // Blocklist enforcement — entries: {i: identifier, m: 'u'|'t', e?: expiresAt}
        var focusBlocked = new List<BlockEntry>();
        DateTime lastFocusToast = DateTime.MinValue;
        NativeTrayIcon? tray = null;

        void ReloadBlocklist()
        {
            focusBlocked.Clear();
            try
            {
                var raw = LiveStatusStore.Settings.FocusBlocklist;
                if (string.IsNullOrWhiteSpace(raw) || raw == "[]") return;
                // Try the new object format first
                var entries = System.Text.Json.JsonSerializer.Deserialize<BlockEntry[]>(raw);
                if (entries is not null) { focusBlocked.AddRange(entries); return; }
            }
            catch { }
            // Fallback: old string[] format — migrate on read
            try
            {
                var legacy = System.Text.Json.JsonSerializer.Deserialize<string[]>(LiveStatusStore.Settings.FocusBlocklist);
                if (legacy is null) return;
                var migrated = legacy.Select(s => new BlockEntry(s, "u", null)).ToArray();
                focusBlocked.AddRange(migrated);
                // Persist migrated format
                var json = System.Text.Json.JsonSerializer.Serialize(migrated);
                LiveStatusStore.Settings = LiveStatusStore.Settings with { FocusBlocklist = json };
            }
            catch { }
        }

        // Initial load with migration
        ReloadBlocklist();

        bool IsBlocked(string exeOrDomain)
        {
            var lower = exeOrDomain.Replace(".exe", "").ToLowerInvariant();
            foreach (var be in focusBlocked)
            {
                var id = be.I.Replace(".exe", "").ToLowerInvariant();
                if (lower == id || lower.EndsWith("." + id)) return true;
            }
            return false;
        }

        string GetBlockAction() => LiveStatusStore.Settings.BlockAction;

        void LogCrash(string message)
        {
            try
            {
                System.IO.File.AppendAllText(
                    Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "TimeLens", "crash.log"),
                    $"{DateTime.UtcNow:o} {message}{Environment.NewLine}");
            }
            catch { }
        }

        void PersistBlocklist()
        {
            var json = System.Text.Json.JsonSerializer.Serialize(focusBlocked.ToArray());
            LiveStatusStore.Settings = LiveStatusStore.Settings with { FocusBlocklist = json };
            var dbDir = Path.GetDirectoryName(dbPath)!;
            var svc = new SettingsService(dbPath);
            svc.Save("focus_blocklist", json);
        }

        void EnforceBlock(string exeName)
        {
            if (!LiveStatusStore.Settings.FocusMode) return;

            var action = GetBlockAction();

            // Always show toast when a blocked app is detected
            if ((DateTime.UtcNow - lastFocusToast).TotalMinutes > 1)
            {
                lastFocusToast = DateTime.UtcNow;
                tray?.ShowBalloon("Focus Mode", $"'{exeName}' is blocked — get back to work!", true);
            }

            if (action == "notify") return; // toast only, no further enforcement

            try
            {
                var exeOnly = System.IO.Path.GetFileNameWithoutExtension(exeName);
                var procs = System.Diagnostics.Process.GetProcessesByName(exeOnly);

                foreach (var proc in procs)
                {
                    try
                    {
                        if (action == "kill" || action == "strict")
                        {
                            proc.Kill(entireProcessTree: true);
                            writer.InsertBlockLog(exeName, action);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogCrash($"EnforceBlock kill '{exeName}' pid={proc.Id}: {ex}");
                    }
                }

                if (action == "hide" || action == "strict")
                {
                    // Minimize any visible windows of this process
                    var windows = Win32.FindWindowsForProcess(exeOnly);
                    foreach (var hwnd in windows)
                    {
                        Win32.ShowWindow(hwnd, Win32.SW_MINIMIZE);
                    }
                }
            }
            catch (Exception ex)
            {
                LogCrash($"EnforceBlock '{exeName}': {ex}");
            }
        }

        // Timer to periodically enforce blocks + auto-remove expired
        var blockTimer = new Timer(_ =>
        {
            if (!LiveStatusStore.Settings.FocusMode) return;

            // Check for expired entries and remove them
            var removed = focusBlocked.RemoveAll(be => be.IsExpired());
            if (removed > 0) PersistBlocklist();

            foreach (var blocked in focusBlocked)
            {
                if (!blocked.I.Contains(".exe")) continue;
                EnforceBlock(blocked.I);
            }
        }, null, 5_000, 5_000);

        var goalDbPath = $"Data Source={dbPath}";
        var goalTimer = new Timer(_ =>
        {
            try
            {
                var today = DateTime.UtcNow.Date.ToString("o");
                using var conn = new Microsoft.Data.Sqlite.SqliteConnection(goalDbPath);
                conn.Open();

                // Query today's active time per app and category
                using var timeCmd = conn.CreateCommand();
                timeCmd.CommandText = """
                    SELECT COALESCE(category,''), exe_name, SUM((julianday(COALESCE(end_time,$now)) - julianday(start_time)) * 86400)
                    FROM app_events
                    WHERE start_time >= $t0 AND session_state = 'active'
                    GROUP BY 1, 2
                    """;
                timeCmd.Parameters.AddWithValue("$t0", today);
                timeCmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
                var times = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var catTimes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                using var tr = timeCmd.ExecuteReader();
                while (tr.Read())
                {
                    var cat = tr.GetString(0);
                    var exe = tr.GetString(1);
                    var secs = tr.IsDBNull(2) ? 0 : (int)Math.Round(tr.GetDouble(2));
                    var mins = secs / 60;
                    if (!string.IsNullOrEmpty(exe)) times[exe] = times.GetValueOrDefault(exe) + mins;
                    if (!string.IsNullOrEmpty(cat)) catTimes[cat] = catTimes.GetValueOrDefault(cat) + mins;
                }

                // Check each active goal
                using var goalCmd = conn.CreateCommand();
                goalCmd.CommandText = "SELECT id, goal_type, target, threshold_minutes, notify_at, COALESCE(last_notified,'') FROM goals WHERE enabled = 1";
                using var gr = goalCmd.ExecuteReader();
                var now = DateTime.UtcNow;
                while (gr.Read())
                {
                    var id = gr.GetInt32(0);
                    var goalType = gr.GetString(1);
                    var target = gr.GetString(2);
                    var threshold = gr.GetInt32(3);
                    var notifyAt = gr.GetInt32(4);
                    var lastNotified = gr.GetString(5);
                    var notifyPct = notifyAt > 0 ? notifyAt : 80;
                    var limit = threshold * notifyPct / 100;
                    var current = goalType == "max_time"
                        ? (catTimes.GetValueOrDefault(target) > 0 ? catTimes.GetValueOrDefault(target) : times.GetValueOrDefault(target))
                        : catTimes.GetValueOrDefault(target);
                    if (current < limit) continue;
                    if (!string.IsNullOrEmpty(lastNotified) && DateTime.TryParse(lastNotified, null, System.Globalization.DateTimeStyles.RoundtripKind, out var ln) && (now - ln).TotalMinutes < 5)
                        continue;
                    tray?.ShowBalloon("Goal Alert", $"'{target}' has reached {current}/{threshold} min today", false);
                    using var upd = conn.CreateCommand();
                    upd.CommandText = "UPDATE goals SET last_notified = $now WHERE id = $id";
                    upd.Parameters.AddWithValue("$now", now.ToString("o"));
                    upd.Parameters.AddWithValue("$id", id);
                    upd.ExecuteNonQuery();
                }
            }
            catch { }
        }, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));

        // Browser exes for which we skip app-level rows when the extension is active.
        // Without this, every tab switch also creates an app row → redundant Browsing entries.
        var browserExes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "chrome.exe", "msedge.exe", "microsoftedge.exe", "firefox.exe",
            "zen.exe", "brave.exe", "opera.exe", "vivaldi.exe"
        };

        bool ShouldSkipBrowserAppRow(string exe) =>
            browserExes.Contains(exe) &&
            (DateTime.UtcNow - LiveStatusStore.LastExtensionHeartbeat).TotalMinutes < 2;

        winWatcher.ForegroundChanged += (exe, title, pid) =>
        {
            if (ShouldSkipBrowserAppRow(exe)) return;

            var cat = classifier.Classify(exe, title);
            var state = idleMonitor.GetState();
            var project = CategoryClassifier.ExtractProject(exe, title);
            writer.OpenAppEvent(exe, title, pid, state, cat, project);
            LiveStatusStore.IsIdle = state != "active";
            LiveStatusStore.IdleSeconds = idleMonitor.IdleSeconds();
            LiveStatusStore.SystemState = state;

            // Focus mode — blocklist check on foreground switch
            if (LiveStatusStore.Settings.FocusMode && state == "active")
            {
                var blocked = IsBlocked(exe);
                if (blocked)
                    EnforceBlock(exe);
            }
        };

        sessionWatcher.StateChanged += state =>
        {
            writer.InsertSessionEvent(state);

            switch (state)
            {
                case "locked":
                case "sleep":
                    LiveStatusStore.SystemState = "away";
                    WriteAppEvent();
                    break;
                case "unlocked":
                case "wake":
                    LiveStatusStore.SystemState = "active";
                    idleMonitor.ResetLastState();
                    WriteAppEvent();
                    break;
            }
        };

        idleMonitor.StateChanged += (from, to) =>
        {
            if (from == "active" && (to == "idle" || to == "away"))
            {
                var (exe, _, _) = Win32.GetForegroundWindowInfo();
                writer.StartIdleSpan(exe, to == "away" ? "away" : "input_idle");
            }
            else if ((from == "idle" || from == "away") && to == "active")
            {
                writer.EndIdleSpan();
            }
        };

        if (settings.TrackInput)
            inputMonitor.InputActivityTick += (keys, clicks, pid, exe) =>
            {
                writer.InsertInputActivity(keys, clicks, pid, exe);
            };

        // Browser processes — audio from these is already tracked by the extension's
        // audible-status endpoint, so skip Core Audio logging to avoid duplicate entries.
        var browserAudioExes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "chrome.exe", "msedge.exe", "microsoftedge.exe", "firefox.exe",
            "zen.exe", "brave.exe", "opera.exe", "vivaldi.exe", "arc.exe", "thorium.exe"
        };

        if (settings.TrackAudio)
            audioMonitor.SessionAudioChanged += (pid, exe, playing) =>
            {
                if (browserAudioExes.Contains(exe ?? "") && !string.IsNullOrEmpty(LiveStatusStore.AudibleTab))
                    return;
                writer.InsertAudioActivity(pid, exe, playing);
                LiveStatusStore.AudioActive = audioMonitor.AnyAudioPlaying;
            };

        // Watchers will be started inside the message loop via StartupRequested
        // so that WinEvent hooks have a running message pump.

        var lastSystemState = idleMonitor.GetState();

        var idleTimer = new Timer(_ =>
        {
            // Focus mode — browser domain block check
            var blocked = LiveStatusStore.PendingFocusBlock;
            if (blocked is not null && LiveStatusStore.Settings.FocusMode)
            {
                LiveStatusStore.PendingFocusBlock = null;
                if ((DateTime.UtcNow - lastFocusToast).TotalMinutes > 5)
                {
                    lastFocusToast = DateTime.UtcNow;
                    tray!.ShowBalloon("Focus Mode", $"'{blocked}' is blocked — get back to work!", true);
                }
            }

            var curState = idleMonitor.GetState();
            var idleSecs = idleMonitor.IdleSeconds();
            LiveStatusStore.IsIdle = curState != "active";
            LiveStatusStore.IdleSeconds = idleSecs;
            LiveStatusStore.SystemState = curState;

            var changed = curState != lastSystemState;

            if (changed)
            {
                if (lastSystemState != "active" && curState == "active")
                    LiveStatusStore.PendingIdleReturn = true;

                lastSystemState = curState;
                var (exe, title, pid) = Win32.GetForegroundWindowInfo();
                if (ShouldSkipBrowserAppRow(exe)) return;
                var cat = classifier.Classify(exe, title);
                var project = CategoryClassifier.ExtractProject(exe, title);
                writer.OpenAppEvent(exe, title, pid, curState, cat, project);
                LiveStatusStore.CurrentApp = exe;
            }
        }, null, 10_000, 10_000);

        // First-run: ask about auto-start, then wire settings save
        var firstRunDone = false;
        using (var frConn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
        {
            frConn.Open();
            using var frCmd = frConn.CreateCommand();
            frCmd.CommandText = "SELECT value FROM settings WHERE key = 'first_run_done'";
            firstRunDone = frCmd.ExecuteScalar() is not null;
        }

        if (!firstRunDone)
        {
            var result = MessageBox(IntPtr.Zero,
                "Start TimeLens automatically when you log in?",
                "TimeLens Setup",
                MB_YESNO | MB_ICONQUESTION);
            var wantAutoStart = result == IDYES;
            AutoStartManager.SetAutoStart(wantAutoStart);
            settingsSvc.Save("auto_start", wantAutoStart ? "true" : "false");

            // Sync LiveStatusStore so GET /api/settings returns the right value
            LiveStatusStore.Settings = LiveStatusStore.Settings with { AutoStart = wantAutoStart };

            settingsSvc.Save("first_run_done", "true");
        }

        using var apiCts = new CancellationTokenSource();
        _ = ApiHost.StartAsync(dbPath, apiCts.Token,
            saveSetting: (k, v) =>
            {
                settingsSvc.Save(k, v);
                if (k == "auto_start")
                    AutoStartManager.SetAutoStart(v == "true");
                if (k == "focus_blocklist")
                {
                    LiveStatusStore.Settings = LiveStatusStore.Settings with { FocusBlocklist = v };
                    ReloadBlocklist();
                }
                if (k == "block_action")
                {
                    LiveStatusStore.Settings = LiveStatusStore.Settings with { BlockAction = v };
                    ReloadBlocklist();
                }
            },
            setTrackAudio: ApplyTrackAudio,
            setTrackInput: ApplyTrackInput,
            upsertRule: UpsertRule,
            deleteRule: DeleteRule,
            enforceBlock: EnforceBlock);

        void OnAudioChanged(int pid, string exe, bool playing)
        {
            if (browserAudioExes.Contains(exe) && !string.IsNullOrEmpty(LiveStatusStore.AudibleTab))
                return;
            writer.InsertAudioActivity(pid, exe, playing);
            LiveStatusStore.AudioActive = audioMonitor.AnyAudioPlaying;
        }

        void OnInputTick(int keys, int clicks, int? pid, string? exe)
        {
            writer.InsertInputActivity(keys, clicks, pid, exe);
        }

        void ApplyTrackAudio(bool on)
        {
            RuntimeConfig.Settings = RuntimeConfig.Settings with { TrackAudio = on };
            LiveStatusStore.Settings = RuntimeConfig.Settings;
            if (on)
            {
                idleMonitor.AudioMonitorRef = audioMonitor;
                audioMonitor.Start();
                audioMonitor.SessionAudioChanged += OnAudioChanged;
            }
            else
            {
                audioMonitor.Stop();
                audioMonitor.SessionAudioChanged -= OnAudioChanged;
                idleMonitor.AudioMonitorRef = null;
            }
        }

        void ApplyTrackInput(bool on)
        {
            RuntimeConfig.Settings = RuntimeConfig.Settings with { TrackInput = on };
            LiveStatusStore.Settings = RuntimeConfig.Settings;
            if (on)
            {
                inputMonitor.Start();
                inputMonitor.InputActivityTick += OnInputTick;
            }
            else
            {
                inputMonitor.Stop();
                inputMonitor.InputActivityTick -= OnInputTick;
            }
        }

        void UpsertRule(string pattern, string category, string ruleType, string target, int priority) => classifier.AddCustomRule(pattern, category, ruleType, target, priority);
        void DeleteRule(string pattern) => classifier.RemoveCustomRule(pattern);

        int consecutiveActiveMinutes = 0;

        using var trayDispose = tray = new NativeTrayIcon();
        tray.StartupRequested += () =>
        {
            winWatcher.Start();
            sessionWatcher.Start();
            if (settings.TrackInput) inputMonitor.Start();
            if (settings.TrackAudio) audioMonitor.Start();

            // Break reminder timer — fires every 60s
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                while (!apiCts.Token.IsCancellationRequested)
                {
                    await System.Threading.Tasks.Task.Delay(60_000, apiCts.Token);
                    var s = LiveStatusStore.Settings;
                    if (!s.BreakReminder) continue;
                    if (LiveStatusStore.IsIdle) { consecutiveActiveMinutes = 0; continue; }
                    consecutiveActiveMinutes++;
                    if (consecutiveActiveMinutes >= s.BreakIntervalMinutes)
                    {
                        tray.ShowBalloon("TimeLens", $"You've been active for {consecutiveActiveMinutes} min — take a break!", false);
                        consecutiveActiveMinutes = 0;
                    }
                }
            }, apiCts.Token);
        };
        tray.OpenDashboardRequested += () =>
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = $"http://127.0.0.1:{TimeLens.Api.ApiHost.DefaultPort}/",
                UseShellExecute = true
            });
        };
        tray.InstallExtensionRequested += () =>
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = $"http://127.0.0.1:{TimeLens.Api.ApiHost.DefaultPort}/extension-setup",
                UseShellExecute = true
            });
        };
        tray.ExitRequested += () =>
        {
            apiCts.Cancel();
            Environment.Exit(0);
        };

        tray.Run();
        mutex?.Dispose();
    }
}
