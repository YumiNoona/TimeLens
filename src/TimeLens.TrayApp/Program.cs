using TimeLens.Api;
using TimeLens.TrayApp.Services;
using TimeLens.TrayApp.Watchers;

namespace TimeLens.TrayApp;

internal static class Program
{
    private const string MutexName = "TimeLens-TrayApp-Instance";

    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, MutexName, out var created);
        if (!created)
            return;

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

        var idleTimer = new Timer(_ =>
        {
            var curState = idleMonitor.GetState();
            var idleSecs = idleMonitor.IdleSeconds();
            LiveStatusStore.IsIdle = curState != "active";
            LiveStatusStore.IdleSeconds = idleSecs;
            LiveStatusStore.SystemState = curState;

            if (curState != lastSystemState)
            {
                if (lastSystemState != "active" && curState == "active")
                    LiveStatusStore.PendingIdleReturn = true;

                lastSystemState = curState;
                var (exe, title, pid) = Win32.GetForegroundWindowInfo();
                var cat = classifier.Classify(exe, title);
                writer.OpenAppEvent(exe, title, pid, curState, cat);
                LiveStatusStore.CurrentApp = exe;
            }
        }, null, 30_000, 30_000);

        AutoStartManager.EnsureAutoStart();

        using var apiCts = new CancellationTokenSource();
        var apiStarted = false;
        var apiIdleTimer = new Timer(_ =>
        {
            if (apiStarted && (DateTime.UtcNow - ApiHost.LastActivityUtc).TotalMinutes > 5)
            {
                apiCts.Cancel();
                apiStarted = false;
            }
        }, null, 60_000, 60_000);

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
            if (!apiStarted)
            {
                apiStarted = true;
                _ = ApiHost.StartAsync(dbPath, apiCts.Token,
                    saveSetting: (k, v) => settingsSvc.Save(k, v),
                    setTrackAudio: ApplyTrackAudio,
                    setTrackInput: ApplyTrackInput,
                    upsertRule: UpsertRule,
                    deleteRule: DeleteRule);
            }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "http://127.0.0.1:47821/",
                UseShellExecute = true
            });
        };
        tray.ExitRequested += () => Environment.Exit(0);

        tray.Run();
    }
}
