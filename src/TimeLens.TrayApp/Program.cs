using System.Runtime.InteropServices;
using TimeLens.Api;
using TimeLens.Api.Services;
using TimeLens.TrayApp.Services;
using TimeLens.TrayApp.Watchers;
using System.Security.Cryptography;

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
        var startupRequested = args.Any(arg => string.Equals(arg, "--startup", StringComparison.OrdinalIgnoreCase));
        var updatedRequested = args.Any(arg => string.Equals(arg, "--updated", StringComparison.OrdinalIgnoreCase));
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

            MainImpl(dataDir, categoriesPath, iconPath, smokeTest, startupRequested, updatedRequested);
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

        if (File.Exists(targetPath) && EmbeddedResourceMatches(resource, targetPath))
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

    private static bool EmbeddedResourceMatches(Stream resource, string targetPath)
    {
        if (new FileInfo(targetPath).Length != resource.Length) return false;
        var embeddedHash = SHA256.HashData(resource);
        resource.Position = 0;
        using var installed = File.OpenRead(targetPath);
        var installedHash = SHA256.HashData(installed);
        return CryptographicOperations.FixedTimeEquals(embeddedHash, installedHash);
    }

    private static void MainImpl(string dataDir, string builtinCsvPath, string iconPath, bool smokeTest, bool startupRequested, bool updatedRequested)
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
        RefreshDefaultCategories(dbPath, classifier);
        var winWatcher = new WinEventWatcher();
        var idleMonitor = new IdleMonitor { IdleThresholdSeconds = settings.IdleThresholdSeconds };
        var sessionWatcher = new SessionWatcher();
        var inputMonitor = new InputMonitor();
        var audioMonitor = new AudioMonitor();

        if (settings.TrackAudio)
            idleMonitor.AudioMonitorRef = audioMonitor;

        bool IsExtensionTrackedBrowser(string exe)
        {
            var normalized = Path.GetFileName(exe).ToLowerInvariant();
            return normalized is "chrome.exe" or "msedge.exe" or "microsoftedge.exe" or "firefox.exe" or
                "zen.exe" or "brave.exe" or "opera.exe" or "vivaldi.exe" or "arc.exe" or "thorium.exe"
                && (DateTime.UtcNow - LiveStatusStore.LastExtensionHeartbeat).TotalMinutes < 2;
        }

        void WriteAppEvent(string? foregroundExe = null, string? foregroundTitle = null, int? foregroundPid = null, string? forcedState = null)
        {
            var (detectedExe, detectedTitle, detectedPid) = Win32.GetForegroundWindowInfo();
            var exe = foregroundExe ?? detectedExe;
            var title = foregroundTitle ?? detectedTitle;
            var pid = foregroundPid ?? detectedPid;
            if (string.Equals(exe, "conhost.exe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(exe, "openconsole.exe", StringComparison.OrdinalIgnoreCase))
                exe = ResolveConsoleExe(title) ?? exe;
            var cat = classifier.Classify(exe, title);
            var state = forcedState ?? idleMonitor.GetState();
            LiveStatusStore.CurrentApp = exe;
            LiveStatusStore.IsIdle = state != "active";
            LiveStatusStore.IdleSeconds = idleMonitor.IdleSeconds();
            LiveStatusStore.SystemState = state;
            if (state != "active" || IsExtensionTrackedBrowser(exe) || cat == "system")
            {
                writer.CloseCurrentAppEvent();
                return;
            }
            var project = CategoryClassifier.ExtractProject(exe, title);
            writer.OpenAppEvent(exe, title, pid, state, cat, project);
        }

        // Blocklist enforcement — entries: {i: identifier, m: 'u'|'t', e?: expiresAt}
        var focusBlockLock = new object();
        BlockEntry[] focusBlocked = [];
        var focusToastTimes = new System.Collections.Concurrent.ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        string? BlockMediaPathForToast(AppSettings currentSettings)
        {
            var fileName = currentSettings.BlockMediaType switch
            {
                "image/jpeg" => "block-notification-media.jpg",
                "image/gif" => "block-notification-media.gif",
                "video/mp4" or "video/webm" => "block-notification-poster.png",
                _ => "block-notification-media.png"
            };
            var path = Path.Combine(dataDir, fileName);
            if (File.Exists(path)) return path;
            var legacyPath = Path.Combine(dataDir, "block-notification.png");
            return File.Exists(legacyPath) ? legacyPath : null;
        }
        NativeTrayIcon? tray = null;

        void ReloadBlocklist()
        {
            var raw = LiveStatusStore.Settings.FocusBlocklist;
            var entries = (BlockEntryHelper.TryParseBlockEntries(raw) ?? [])
                .Where(entry => !entry.IsExpired() &&
                    !BlockEntryHelper.IsProtected(entry.I) &&
                    !BlockEntryHelper.IsUnsafeShellAction(entry, LiveStatusStore.Settings.BlockAction))
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

        BlockEntry? FindBlockedExecutable(string executable) =>
            BlocklistSnapshot().FirstOrDefault(entry => !entry.IsExpired() &&
                BlockEntryHelper.MatchesExecutable(entry, executable));

        void ShowBlockToast(string target, string? action = null, bool force = false)
        {
            var now = DateTime.UtcNow;
            if (!force)
            {
                var previous = focusToastTimes.GetOrAdd(target, DateTime.MinValue);
                if ((now - previous).TotalSeconds < Math.Clamp(LiveStatusStore.Settings.BlockNotifyIntervalSeconds, 5, 86400)) return;
                focusToastTimes[target] = now;
            }

            var currentSettings = LiveStatusStore.Settings;
            var effectiveAction = BlockActionPlan.From(action ?? currentSettings.BlockAction).Id;
            var imagePath = string.IsNullOrEmpty(currentSettings.BlockImageVersion)
                ? null
                : BlockMediaPathForToast(currentSettings);
            try
            {
                tray?.ShowBalloon(
                    BlockNotification.FormatTitle(currentSettings.BlockTitle, target, effectiveAction),
                    BlockNotification.Format(currentSettings.BlockMessage, target, effectiveAction),
                    warning: true,
                    imagePath: imagePath,
                    position: currentSettings.BlockNotifyPosition,
                    mediaLayout: currentSettings.BlockMediaLayout);
            }
            catch { }
        }

        static bool IsExecutableRunning(string executable)
        {
            try
            {
                var processName = Path.GetFileNameWithoutExtension(executable);
                var processes = System.Diagnostics.Process.GetProcessesByName(processName);
                foreach (var process in processes) process.Dispose();
                return processes.Length > 0;
            }
            catch { return false; }
        }

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
                BlockEntryHelper.IsProtected(normalized))
                return false;

            var entry = FindBlockedExecutable(normalized);
            if (entry is null) return false;
            if (BlockEntryHelper.IsUnsafeShellAction(entry, LiveStatusStore.Settings.BlockAction)) return false;
            var plan = BlockActionPlan.From(BlockEntryHelper.ActionFor(entry, LiveStatusStore.Settings.BlockAction));
            ShowBlockToast(normalized, plan.Id);

            try
            {
                var exeOnly = System.IO.Path.GetFileNameWithoutExtension(normalized);
                var procs = System.Diagnostics.Process.GetProcessesByName(exeOnly);
                bool MinimizeWindows()
                {
                    var minimized = false;
                    var windows = Win32.FindWindowsForProcess(exeOnly);
                    foreach (var hwnd in windows)
                    {
                        if (!Win32.IsIconic(hwnd))
                        {
                            Win32.ShowWindow(hwnd, Win32.SW_MINIMIZE);
                            minimized = true;
                        }
                    }
                    return minimized;
                }

                bool TerminateProcesses()
                {
                    var terminated = false;
                    foreach (var proc in procs)
                    {
                        using (proc)
                        {
                            try
                            {
                                if (proc.Id != Environment.ProcessId)
                                {
                                    proc.Kill(entireProcessTree: true);
                                    terminated = true;
                                }
                            }
                            catch (Exception ex)
                            {
                                LogCrash($"EnforceBlock kill '{normalized}' pid={proc.Id}: {ex}");
                            }
                        }
                    }
                    return terminated;
                }

                // Strict minimizes first so the hide part is effective even if process
                // termination is delayed or denied by Windows.
                var intervened = BlockEnforcement.Apply(plan, procs.Length > 0, MinimizeWindows, TerminateProcesses);
                if (!plan.TerminateProcesses)
                    foreach (var proc in procs) proc.Dispose();
                if (intervened) writer.InsertBlockLog(normalized, plan.Id);
            }
            catch (Exception ex)
            {
                LogCrash($"EnforceBlock '{normalized}': {ex}");
            }
            return true;
        }

        // Timer to periodically enforce blocks + auto-remove expired
        using var blockTimer = new Timer(_ =>
        {
            try
            {
                var before = BlocklistSnapshot();
                var active = before.Where(entry => !entry.IsExpired()).ToArray();
                if (active.Length != before.Length)
                {
                    lock (focusBlockLock) focusBlocked = active;
                    PersistBlocklist();
                }

                if (!LiveStatusStore.Settings.FocusMode) return;

                foreach (var blocked in active)
                {
                    if (!BlockEntryHelper.IsExecutable(blocked)) continue;
                    if (!BlockActionPlan.From(BlockEntryHelper.ActionFor(blocked, LiveStatusStore.Settings.BlockAction)).RepeatEveryFiveSeconds) continue;
                    if (!IsExecutableRunning(blocked.I)) continue;
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
        using var goalTimer = new Timer(_ =>
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

        winWatcher.ForegroundChanged += (exe, title, pid) =>
        {
            WriteAppEvent(exe, title, pid);

            // Focus mode — blocklist check on foreground switch
            if (LiveStatusStore.Settings.FocusMode && !LiveStatusStore.IsIdle)
            {
                var blocked = FindBlockedExecutable(exe) is not null;
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
                writer.CloseCurrentAppEvent();
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

        using var idleTimer = new Timer(_ =>
        {
            // Focus mode — browser domain block check
            var blocked = LiveStatusStore.PendingBrowserBlock;
            if (blocked is not null && LiveStatusStore.Settings.FocusMode)
            {
                LiveStatusStore.PendingBrowserBlock = null;
                writer.InsertBlockLog(blocked.Target, blocked.Action);
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
                if (curState == "active") WriteAppEvent(forcedState: curState);
                else writer.CloseCurrentAppEvent();
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
            if (startupRequested)
            {
                // A Run-key launch must stay silent: do not put a first-run prompt in
                // front of the user's desktop while Windows is signing in.
                var autoStartEnabled = AutoStartManager.IsAutoStartEnabled();
                settingsSvc.Save("auto_start", autoStartEnabled ? "true" : "false");
                LiveStatusStore.Settings = LiveStatusStore.Settings with { AutoStart = autoStartEnabled };
                settingsSvc.Save("first_run_done", "true");
                return;
            }
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
                if (k == "block_title")
                    LiveStatusStore.Settings = LiveStatusStore.Settings with { BlockTitle = BlockNotification.NormalizeTitle(v) };
                if (k == "block_message")
                    LiveStatusStore.Settings = LiveStatusStore.Settings with { BlockMessage = BlockNotification.NormalizeMessage(v) };
                if (k == "block_image_version")
                    LiveStatusStore.Settings = LiveStatusStore.Settings with { BlockImageVersion = v };
                if (k == "block_media_type")
                    LiveStatusStore.Settings = LiveStatusStore.Settings with { BlockMediaType = v };
            },
            setTrackAudio: ApplyTrackAudio,
            setTrackInput: ApplyTrackInput,
            upsertRule: UpsertRule,
            deleteRule: DeleteRule,
            enforceBlock: EnforceBlock,
            showBlockPreview: target => ShowBlockToast(target, force: true),
            recordBlockAttempt: writer.InsertBlockLog,
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
        tray.ToastFailed += ex => LogCrash($"Toast: {ex}");
        var executablePath = Environment.ProcessPath;
        var dashboardBuildKey = executablePath is not null && File.Exists(executablePath)
            ? File.GetLastWriteTimeUtc(executablePath).Ticks.ToString("x", System.Globalization.CultureInfo.InvariantCulture)
            : DateTime.UtcNow.Ticks.ToString("x", System.Globalization.CultureInfo.InvariantCulture);
        void OpenDashboard()
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = $"http://127.0.0.1:{TimeLens.Api.ApiHost.DefaultPort}/?v={dashboardBuildKey}",
                UseShellExecute = true
            });
        }
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

            if (updatedRequested)
            {
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        await System.Threading.Tasks.Task.Delay(1_500, apiCts.Token);
                        OpenDashboard();
                    }
                    catch (OperationCanceledException) { }
                }, apiCts.Token);
            }
        };
        tray.OpenDashboardRequested += () =>
        {
            OpenDashboard();
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
            if (LiveStatusStore.Settings.BlockProtectionEnabled && LiveStatusStore.Settings.BlockExitProtection)
            {
                tray.ShowBalloon("TimeLens is protected", "Unlock protected blocks from the Block page before exiting.", true);
                return;
            }
            RequestShutdown();
        };

        tray.Run();
    }

    private static void RefreshDefaultCategories(string dbPath, CategoryClassifier classifier)
    {
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        var uncategorized = new List<(long Id, string Exe, string Title)>();
        using (var select = conn.CreateCommand())
        {
            select.CommandText = "SELECT id, exe_name, COALESCE(window_title, '') FROM app_events WHERE category IS NULL OR lower(category) = 'other'";
            using var reader = select.ExecuteReader();
            while (reader.Read()) uncategorized.Add((reader.GetInt64(0), reader.GetString(1), reader.GetString(2)));
        }
        if (uncategorized.Count == 0) return;

        using var transaction = conn.BeginTransaction();
        using var update = conn.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = "UPDATE app_events SET category = $category WHERE id = $id";
        var category = update.CreateParameter(); category.ParameterName = "$category"; update.Parameters.Add(category);
        var id = update.CreateParameter(); id.ParameterName = "$id"; update.Parameters.Add(id);
        foreach (var entry in uncategorized)
        {
            var resolved = classifier.Classify(entry.Exe, entry.Title);
            if (resolved == "other") continue;
            category.Value = resolved;
            id.Value = entry.Id;
            update.ExecuteNonQuery();
        }
        transaction.Commit();
    }
}
