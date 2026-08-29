using System.Runtime.InteropServices;
using TimeLens.Api;
using TimeLens.Api.Services;
using TimeLens.TrayApp.Services;
using TimeLens.TrayApp.Watchers;

namespace TimeLens.TrayApp;

internal static class Program
{
    private const string MutexName = "TimeLens-TrayApp-Instance";
    private const int MB_YESNO = 0x04;
    private const int MB_ICONQUESTION = 0x20;
    private const int IDYES = 6;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    [STAThread]
    private static void Main(string[] args)
    {
        // The release smoke test runs the real startup with isolated data, no setup
        // prompts, and no registry changes. Normal launches always use LocalAppData.
        var smokeTestIndex = Array.IndexOf(args, "--smoke-test");
        var smokeTest = smokeTestIndex >= 0;
        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TimeLens");
        using var instanceMutex = new Mutex(true, MutexName, out var isFirstInstance);
        if (!isFirstInstance)
            return;

        try
        {
            if (smokeTest)
            {
                if (smokeTestIndex + 1 >= args.Length)
                    throw new ArgumentException("--smoke-test requires a data directory.");
                dataDir = Path.GetFullPath(args[smokeTestIndex + 1]);
                if (Directory.Exists(dataDir) || File.Exists(dataDir))
                    throw new InvalidOperationException("Smoke tests require a new, empty data directory.");
            }
            // Native AOT cannot load SQLite directly from an assembly resource. Extract the
            // embedded runtime payload on first launch so TimeLens.exe remains copy-and-run.
            var sqlitePath = EnsureRuntimeFile(dataDir, "runtime/e_sqlite3.dll", "e_sqlite3.dll");
            var categoriesPath = EnsureRuntimeFile(dataDir, "runtime/categories.csv", "categories.csv");
            var iconPath = EnsureRuntimeFile(dataDir, "runtime/TimeLens.ico", "TimeLens.ico");
            NativeLibrary.Load(sqlitePath);

            MainImpl(dataDir, categoriesPath, iconPath, smokeTest);
        }
        catch (Exception ex)
        {
            var logPath = Path.Combine(dataDir, "crash.log");
            var logWritten = false;
            try
            {
                Directory.CreateDirectory(dataDir);
                File.AppendAllText(logPath,
                    $"{DateTime.UtcNow:o} Fatal: {ex}{Environment.NewLine}");
                logWritten = true;
            }
            catch { /* A logging failure must not hide the original startup error. */ }
            if (!smokeTest) MessageBox(IntPtr.Zero,
                $"TimeLens could not start.\n\n{ex.Message}\n\n" +
                (logWritten ? $"Diagnostic details: {logPath}" : "The diagnostic log could not be written."),
                "TimeLens startup error", 0x10);
            Environment.Exit(1);
        }
    }

    private static string EnsureRuntimeFile(string dataDir, string resourceName, string fileName)
    {
        var runtimeDir = Path.Combine(dataDir, "runtime");
        Directory.CreateDirectory(runtimeDir);

        var targetPath = Path.Combine(runtimeDir, fileName);
        using var resource = typeof(Program).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded runtime resource is missing: {resourceName}");

        if (File.Exists(targetPath) && new FileInfo(targetPath).Length == resource.Length)
            return targetPath;

        var temporaryPath = targetPath + ".new";
        try
        {
            using (var output = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
                resource.CopyTo(output);
            File.Move(temporaryPath, targetPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }

        return targetPath;
    }

    private static void MainImpl(string dataDir, string builtinCsvPath, string iconPath, bool smokeTest)
    {
        var dbPath = Path.Combine(dataDir, "activity.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        var settingsSvc = new SettingsService(dbPath);
        var settings = DatabaseInitializer.Initialize(dbPath);
        if (!smokeTest)
        {
            if (settings.AutoStart)
            {
                // Repair stale paths after moving/reinstalling TimeLens or updating from
                // an older startup entry that did not include the explicit startup switch.
                if (!AutoStartManager.IsAutoStartEnabled() &&
                    !AutoStartManager.TrySetAutoStart(true, out _))
                {
                    settingsSvc.Save("auto_start", "false");
                    settings = settings with { AutoStart = false };
                }
            }
            else if (AutoStartManager.IsAutoStartEnabled())
            {
                // The installer can enable startup before the database has been created.
                settingsSvc.Save("auto_start", "true");
                settings = settings with { AutoStart = true };
            }
        }
        RuntimeConfig.Settings = settings;
        LiveStatusStore.Settings = settings;

        var writer = new EventWriter(dbPath);
        var classifier = new CategoryClassifier();

        // Load community built-in rules first (lowest priority, overridden by user rules)
        var userCsvPath = Path.Combine(dataDir, "categories.csv");
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
        var focusBlockLock = new object();
        BlockEntry[] focusBlocked = [];
        DateTime lastFocusToast = DateTime.MinValue;
        NativeTrayIcon? tray = null;

        void ReloadBlocklist()
        {
            var raw = LiveStatusStore.Settings.FocusBlocklist;
            var entries = (BlockEntryHelper.TryParseBlockEntries(raw) ?? [])
                .Where(entry => !entry.IsExpired() && !BlockEntryHelper.IsProtected(entry.I))
                .ToArray();
            lock (focusBlockLock) focusBlocked = entries;

            var canonical = BlockEntryHelper.Serialize(entries);
            if (!string.Equals(raw, canonical, StringComparison.Ordinal))
            {
                LiveStatusStore.Settings = LiveStatusStore.Settings with { FocusBlocklist = canonical };
                settingsSvc.Save("focus_blocklist", canonical);
            }
        }

        // Initial load with migration
        ReloadBlocklist();

        BlockEntry[] BlocklistSnapshot()
        {
            lock (focusBlockLock) return focusBlocked.ToArray();
        }

        bool IsBlockedExecutable(string executable) =>
            BlocklistSnapshot().Any(entry => !entry.IsExpired() &&
                BlockEntryHelper.MatchesExecutable(entry, executable));

        string GetBlockAction() => LiveStatusStore.Settings.BlockAction;

        void LogCrash(string message)
        {
            try
            {
                System.IO.File.AppendAllText(
                    Path.Combine(dataDir, "crash.log"),
                    $"{DateTime.UtcNow:o} {message}{Environment.NewLine}");
            }
            catch { }
        }

        // Resolve conhost/OpenConsole to the actual console process (cmd/powershell/pwsh).
        // NOTE: This is a heuristic based on English window titles. It will silently
        // fail for non-English Windows installs or if Microsoft changes console title strings.
        // A more robust approach would be walking the process tree, but title-matching
        // covers the common cases well enough for now.
        string? ResolveConsoleExe(string? title)
        {
            if (string.IsNullOrEmpty(title)) return null;
            var t = title;
            // Strip "Administrator: " prefix
            if (t.StartsWith("Administrator:", StringComparison.OrdinalIgnoreCase))
                t = t["Administrator:".Length..].TrimStart();
            if (t.StartsWith("Select", StringComparison.OrdinalIgnoreCase))
                t = t["Select".Length..].TrimStart();

            if (t.StartsWith("Command Prompt", StringComparison.OrdinalIgnoreCase) ||
                t.StartsWith("cmd", StringComparison.OrdinalIgnoreCase))
                return "cmd.exe";
            if (t.Contains("PowerShell", StringComparison.OrdinalIgnoreCase))
            {
                if (t.Contains("7", StringComparison.Ordinal) || t.Contains("pwsh", StringComparison.OrdinalIgnoreCase))
                    return "pwsh.exe";
                return "powershell.exe";
            }
            if (t.Contains("Windows Terminal", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("wt", StringComparison.OrdinalIgnoreCase))
                return "wt.exe";
            return null;
        }

        void PersistBlocklist()
        {
            var json = BlockEntryHelper.Serialize(BlocklistSnapshot());
            LiveStatusStore.Settings = LiveStatusStore.Settings with { FocusBlocklist = json };
            settingsSvc.Save("focus_blocklist", json);
        }

        bool EnforceBlock(string exeName)
        {
            var normalized = BlockEntryHelper.NormalizeIdentifier(exeName);
            if (!LiveStatusStore.Settings.FocusMode || normalized is null ||
                !normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                BlockEntryHelper.IsProtected(normalized) || !IsBlockedExecutable(normalized))
                return false;

            var action = GetBlockAction();
            if (action is not ("notify" or "hide" or "kill" or "strict")) action = "hide";

            // Always show toast when a blocked app is detected
            if ((DateTime.UtcNow - lastFocusToast).TotalMinutes > 1)
            {
                lastFocusToast = DateTime.UtcNow;
                try { tray?.ShowBalloon("Focus Mode", $"'{normalized}' is blocked — get back to work!", true); } catch { }
            }

            if (action == "notify") return true;

            try
            {
                var exeOnly = System.IO.Path.GetFileNameWithoutExtension(normalized);
                var procs = System.Diagnostics.Process.GetProcessesByName(exeOnly);
                var enforced = false;

                foreach (var proc in procs)
                {
                    using (proc)
                    {
                        try
                        {
                            if ((action == "kill" || action == "strict") && proc.Id != Environment.ProcessId)
                            {
                                proc.Kill(entireProcessTree: true);
                                enforced = true;
                            }
                        }
                        catch (Exception ex)
                        {
                            LogCrash($"EnforceBlock kill '{normalized}' pid={proc.Id}: {ex}");
                        }
                    }
                }

                if (action == "hide" || action == "strict")
                {
                    // Minimize any visible windows of this process
                    var windows = Win32.FindWindowsForProcess(exeOnly);
                    foreach (var hwnd in windows)
                    {
                        if (!Win32.IsIconic(hwnd))
                        {
                            Win32.ShowWindow(hwnd, Win32.SW_MINIMIZE);
                            enforced = true;
                        }
                    }
                }

                if (enforced) writer.InsertBlockLog(normalized, action);
            }
            catch (Exception ex)
            {
                LogCrash($"EnforceBlock '{normalized}': {ex}");
            }
            return true;
        }

        // Timer to periodically enforce blocks + auto-remove expired
        var blockTimer = new Timer(_ =>
        {
            try
            {
                if (!LiveStatusStore.Settings.FocusMode) return;

                var before = BlocklistSnapshot();
                var active = before.Where(entry => !entry.IsExpired()).ToArray();
                if (active.Length != before.Length)
                {
                    lock (focusBlockLock) focusBlocked = active;
                    PersistBlocklist();
                }

                foreach (var blocked in active)
                {
                    if (!BlockEntryHelper.IsExecutable(blocked)) continue;
                    EnforceBlock(blocked.I);
                }
            }
            catch (Exception ex)
            {
                // Never let an unhandled exception kill the periodic timer silently
                LogCrash($"blockTimer: {ex}");
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

            // Resolve conhost/OpenConsole to the actual console process (cmd/powershell/pwsh).
            // conhost.exe is the window owner for all console windows — the real target
            // is the shell process attached to it. Resolve by title to match blocklist entries.
            if (string.Equals(exe, "conhost.exe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(exe, "openconsole.exe", StringComparison.OrdinalIgnoreCase))
            {
                exe = ResolveConsoleExe(title) ?? exe;
            }

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
                var blocked = IsBlockedExecutable(exe);
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

        // Browser processes — audio from these is already tracked by the extension's
        // audible-status endpoint, so skip Core Audio logging to avoid duplicate entries.
        var browserAudioExes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "chrome.exe", "msedge.exe", "microsoftedge.exe", "firefox.exe",
            "zen.exe", "brave.exe", "opera.exe", "vivaldi.exe", "arc.exe", "thorium.exe"
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

        void CompleteFirstRun()
        {
            if (firstRunDone || smokeTest) return;
            var result = MessageBox(IntPtr.Zero,
                "Start TimeLens automatically when you log in?",
                "TimeLens Setup",
                MB_YESNO | MB_ICONQUESTION);
            var wantAutoStart = result == IDYES;
            if (!AutoStartManager.TrySetAutoStart(wantAutoStart, out var error))
            {
                MessageBox(IntPtr.Zero,
                    $"Windows could not update the startup setting.\n\n{error}",
                    "TimeLens startup setting", 0x10);
            }
            var actualAutoStart = AutoStartManager.IsAutoStartEnabled();
            settingsSvc.Save("auto_start", actualAutoStart ? "true" : "false");

            // Sync LiveStatusStore so GET /api/settings returns the right value
            LiveStatusStore.Settings = LiveStatusStore.Settings with { AutoStart = actualAutoStart };

            settingsSvc.Save("first_run_done", "true");
        }

        using var apiCts = new CancellationTokenSource();
        using var updateService = new UpdateService();
        void RequestShutdown()
        {
            apiCts.Cancel();
            tray?.Close();
        }
        _ = ApiHost.StartAsync(dbPath, apiCts.Token,
            saveSetting: (k, v) =>
            {
                if (k == "auto_start")
                {
                    if (!AutoStartManager.TrySetAutoStart(v == "true", out var error))
                        throw new InvalidOperationException($"Windows could not update the startup setting: {error}");
                }
                settingsSvc.Save(k, v);
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
            enforceBlock: EnforceBlock,
            updateService: updateService,
            requestShutdown: RequestShutdown);

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
            LiveStatusStore.Settings = LiveStatusStore.Settings with { TrackAudio = on };
            RuntimeConfig.Settings = LiveStatusStore.Settings;
            audioMonitor.SessionAudioChanged -= OnAudioChanged;
            if (on)
            {
                idleMonitor.AudioMonitorRef = audioMonitor;
                audioMonitor.SessionAudioChanged += OnAudioChanged;
                audioMonitor.Start();
            }
            else
            {
                audioMonitor.Stop();
                idleMonitor.AudioMonitorRef = null;
            }
        }

        void ApplyTrackInput(bool on)
        {
            LiveStatusStore.Settings = LiveStatusStore.Settings with { TrackInput = on };
            RuntimeConfig.Settings = LiveStatusStore.Settings;
            inputMonitor.InputActivityTick -= OnInputTick;
            if (on)
            {
                inputMonitor.InputActivityTick += OnInputTick;
                inputMonitor.Start();
            }
            else
            {
                inputMonitor.Stop();
            }
        }

        void UpsertRule(string pattern, string category, string ruleType, string target, int priority) => classifier.AddCustomRule(pattern, category, ruleType, target, priority);
        void DeleteRule(string pattern) => classifier.RemoveCustomRule(pattern);

        int consecutiveActiveMinutes = 0;

        using var trayDispose = tray = new NativeTrayIcon(iconPath);
        var executablePath = Environment.ProcessPath;
        var dashboardBuildKey = executablePath is not null && File.Exists(executablePath)
            ? File.GetLastWriteTimeUtc(executablePath).Ticks.ToString("x", System.Globalization.CultureInfo.InvariantCulture)
            : DateTime.UtcNow.Ticks.ToString("x", System.Globalization.CultureInfo.InvariantCulture);
        if (settings.TrackInput) inputMonitor.InputActivityTick += OnInputTick;
        if (settings.TrackAudio) audioMonitor.SessionAudioChanged += OnAudioChanged;
        tray.StartupRequested += () =>
        {
            // Show the tray icon before any modal first-run setup dialog.
            CompleteFirstRun();
            winWatcher.Start();
            sessionWatcher.Start();
            if (settings.TrackInput) inputMonitor.Start();
            if (settings.TrackAudio) audioMonitor.Start();

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    if (smokeTest) return;
                    await System.Threading.Tasks.Task.Delay(5_000, apiCts.Token);
                    var update = await updateService.CheckAsync(apiCts.Token);
                    if (update.UpdateAvailable && update.LatestVersion is not null)
                        tray.ShowBalloon("TimeLens update available", $"Version {update.LatestVersion} is ready. Open Settings to install it.");
                }
                catch (OperationCanceledException) { }
            }, apiCts.Token);

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
                FileName = $"http://127.0.0.1:{TimeLens.Api.ApiHost.DefaultPort}/?v={dashboardBuildKey}",
                UseShellExecute = true
            });
        };
        tray.InstallExtensionRequested += () =>
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://addons.mozilla.org/en-US/firefox/addon/timelens-tracker/",
                UseShellExecute = true
            });
        };
        tray.ExitRequested += () =>
        {
            RequestShutdown();
        };

        tray.Run();
    }
}
