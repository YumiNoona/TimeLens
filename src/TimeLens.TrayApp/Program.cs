using System.Runtime.InteropServices;
using TimeLens.Api;
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
    private static void Main()
    {
        try
        {
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

        DatabaseInitializer.Initialize(dbPath);

        var settingsSvc = new SettingsService(dbPath);
        var settings = settingsSvc.Load();
        RuntimeConfig.Settings = settings;
        LiveStatusStore.Settings = settings;

        var writer = new EventWriter(dbPath);
        var classifier = new CategoryClassifier();
        using (var loadConn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
        {
            loadConn.Open();
            using var loadCmd = loadConn.CreateCommand();
            loadCmd.CommandText = "SELECT exe_pattern, category FROM custom_rules";
            using var reader = loadCmd.ExecuteReader();
            while (reader.Read())
                classifier.AddCustomRule(reader.GetString(0), reader.GetString(1));
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
            var cat = classifier.Classify(exe, title);
            var state = idleMonitor.GetState();
            writer.OpenAppEvent(exe, title, pid, state, cat);
            LiveStatusStore.CurrentApp = exe;
            LiveStatusStore.IsIdle = state != "active";
            LiveStatusStore.IdleSeconds = idleMonitor.IdleSeconds();
            LiveStatusStore.SystemState = state;
        }

        winWatcher.ForegroundChanged += (exe, title, pid) =>
        {
            var cat = classifier.Classify(exe, title);
            var state = idleMonitor.GetState();
            writer.OpenAppEvent(exe, title, pid, state, cat);
            LiveStatusStore.CurrentApp = exe;
            LiveStatusStore.IsIdle = state != "active";
            LiveStatusStore.IdleSeconds = idleMonitor.IdleSeconds();
            LiveStatusStore.SystemState = state;
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
                    WriteAppEvent();
                    break;
            }
        };

        if (settings.TrackInput)
            inputMonitor.InputActivityTick += (keys, clicks, pid, exe) =>
            {
                writer.InsertInputActivity(keys, clicks, pid, exe);
            };

        if (settings.TrackAudio)
            audioMonitor.SessionAudioChanged += (pid, exe, playing) =>
            {
                writer.InsertAudioActivity(pid, exe, playing);
                LiveStatusStore.AudioActive = audioMonitor.AnyAudioPlaying;
            };

        winWatcher.Start();
        sessionWatcher.Start();
        if (settings.TrackInput) inputMonitor.Start();
        if (settings.TrackAudio) audioMonitor.Start();

        var lastSystemState = idleMonitor.GetState();
        var lastWriteUtc = DateTime.UtcNow;

        var idleTimer = new Timer(_ =>
        {
            var curState = idleMonitor.GetState();
            var idleSecs = idleMonitor.IdleSeconds();
            LiveStatusStore.IsIdle = curState != "active";
            LiveStatusStore.IdleSeconds = idleSecs;
            LiveStatusStore.SystemState = curState;

            var changed = curState != lastSystemState;
            var overdue = (DateTime.UtcNow - lastWriteUtc).TotalMinutes >= 5;

            if (changed || overdue)
            {
                if (changed && lastSystemState != "active" && curState == "active")
                    LiveStatusStore.PendingIdleReturn = true;

                if (changed)
                    lastSystemState = curState;
                lastWriteUtc = DateTime.UtcNow;
                var (exe, title, pid) = Win32.GetForegroundWindowInfo();
                var cat = classifier.Classify(exe, title);
                writer.OpenAppEvent(exe, title, pid, curState, cat);
                LiveStatusStore.CurrentApp = exe;
            }
        }, null, 30_000, 30_000);

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
            },
            setTrackAudio: ApplyTrackAudio,
            setTrackInput: ApplyTrackInput,
            upsertRule: UpsertRule,
            deleteRule: DeleteRule);

        void OnAudioChanged(int pid, string exe, bool playing)
        {
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

        void UpsertRule(string pattern, string category) => classifier.AddCustomRule(pattern, category);
        void DeleteRule(string pattern) => classifier.RemoveCustomRule(pattern);

        using var tray = new NativeTrayIcon();
        tray.OpenDashboardRequested += () =>
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "http://127.0.0.1:47821/",
                UseShellExecute = true
            });
        };
        tray.InstallExtensionRequested += () =>
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "http://127.0.0.1:47821/extension-setup",
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
