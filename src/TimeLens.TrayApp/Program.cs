using System.Runtime.InteropServices;
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

        var writer = new EventWriter(dbPath);
        var classifier = new CategoryClassifier();
        var winWatcher = new WinEventWatcher();
        var idleMonitor = new IdleMonitor();
        var sessionWatcher = new SessionWatcher();
        var inputMonitor = new InputMonitor();
        var audioMonitor = new AudioMonitor();

        idleMonitor.AudioMonitorRef = audioMonitor;

        winWatcher.ForegroundChanged += (exe, title, pid) =>
        {
            var cat = classifier.Classify(exe, title);
            var isIdle = idleMonitor.IsIdle();
            writer.OpenAppEvent(exe, title, pid, isIdle, cat);

            LiveStatusStore.CurrentApp = exe;
            LiveStatusStore.IsIdle = isIdle;
            LiveStatusStore.IdleSeconds = idleMonitor.IdleSeconds();
        };

        sessionWatcher.StateChanged += state =>
        {
            writer.InsertSessionEvent(state);
        };

        inputMonitor.InputActivityTick += (keys, clicks, pid, exe) =>
        {
            writer.InsertInputActivity(keys, clicks, pid, exe);
        };

        audioMonitor.SessionAudioChanged += (pid, exe, playing) =>
        {
            writer.InsertAudioActivity(pid, exe, playing);
            LiveStatusStore.AudioActive = audioMonitor.AnyAudioPlaying;
        };

        winWatcher.Start();
        sessionWatcher.Start();
        inputMonitor.Start();
        audioMonitor.Start();

        var lastWasIdle = idleMonitor.IsIdle();

        var idleTimer = new Timer(_ =>
        {
            var isIdle = idleMonitor.IsIdle();
            LiveStatusStore.IsIdle = isIdle;
            LiveStatusStore.IdleSeconds = idleMonitor.IdleSeconds();

            if (isIdle != lastWasIdle)
            {
                lastWasIdle = isIdle;
                var (exe, title, pid) = GetForegroundWindowInfo();
                var cat = classifier.Classify(exe, title);
                writer.OpenAppEvent(exe, title, pid, isIdle, cat);
                LiveStatusStore.CurrentApp = exe;
            }
        }, null, 8000, 8000);

        _ = ApiHost.StartAsync(dbPath);

        AutoStartManager.EnsureAutoStart();

        using var tray = new NativeTrayIcon();
        tray.OpenDashboardRequested += () =>
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "http://127.0.0.1:47821/",
                UseShellExecute = true
            });
        };
        tray.ExitRequested += () => Environment.Exit(0);

        tray.Run();
    }

    private static (string exe, string title, int pid) GetForegroundWindowInfo()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return ("unknown", "", 0);

        var sb = new System.Text.StringBuilder(256);
        GetWindowText(hwnd, sb, sb.Capacity);
        var title = sb.ToString();

        GetWindowThreadProcessId(hwnd, out var pid);

        try
        {
            using var proc = System.Diagnostics.Process.GetProcessById((int)pid);
            return (proc.ProcessName + ".exe", title, (int)pid);
        }
        catch
        {
            return ("unknown", title, (int)pid);
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
