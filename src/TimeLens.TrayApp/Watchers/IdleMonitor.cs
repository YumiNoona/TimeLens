using System.Runtime.InteropServices;

namespace TimeLens.TrayApp.Watchers;

public sealed class IdleMonitor
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    public AudioMonitor? AudioMonitorRef;

    public int IdleThresholdSeconds { get; set; } = 180;

    // Maximum seconds audio can sustain "active" state without confirmed user input.
    // Once exceeded, audio alone is not enough — the session is considered idle.
    public int AudioSustainedMaxSeconds { get; set; } = 7200; // 2 hours

    public event Action<string, string>? StateChanged;

    private string _lastState = "active";

    private bool _locked;
    private bool _suspended;

    public void SetSessionState(string state)
    {
        if (state == "locked") _locked = true;
        if (state == "unlocked") _locked = false;
        if (state == "sleep") _suspended = true;
        if (state == "wake") _suspended = false;
    }

    private bool IsAudioActive()
    {
        // Background music must not turn hours away from a silent editor into work.
        if (AudioMonitorRef is not null && AudioMonitorRef.IsPlayingFor(TimeLens.Api.LiveStatusStore.CurrentApp)) return true;
        if (TimeLens.Api.LiveStatusStore.Settings.TrackBrowser &&
            (DateTime.UtcNow - TimeLens.Api.LiveStatusStore.LastExtensionHeartbeat).TotalSeconds < 30 &&
            !string.IsNullOrEmpty(TimeLens.Api.LiveStatusStore.AudibleTab) &&
            IsBrowser(TimeLens.Api.LiveStatusStore.CurrentApp)) return true;
        return false;
    }

    private static bool IsBrowser(string exe) => exe.ToLowerInvariant() is
        "chrome.exe" or "msedge.exe" or "firefox.exe" or "zen.exe" or "brave.exe" or
        "opera.exe" or "vivaldi.exe" or "arc.exe" or "thorium.exe";

    private readonly Func<long> _idleMilliseconds;

    public IdleMonitor(Func<long>? idleMilliseconds = null)
    {
        _idleMilliseconds = idleMilliseconds ?? ReadIdleMilliseconds;
    }

    private static long ReadIdleMilliseconds()
    {
        var lii = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref lii)) return 0;
        // Unsigned subtraction handles the 32-bit tick wrap. A slightly future
        // input timestamp must not turn into 49 days of inactivity.
        var elapsed = unchecked((uint)Environment.TickCount64 - lii.dwTime);
        return elapsed > int.MaxValue ? 0 : elapsed;
    }

    public bool IsIdle()
    {
        var threshold = IsAudioActive() ? AudioSustainedMaxSeconds : IdleThresholdSeconds;
        return _idleMilliseconds() >= Math.Max(1L, threshold) * 1000;
    }

    // Report real time since input even when foreground playback sustains activity.
    public int IdleSeconds() => (int)Math.Clamp(_idleMilliseconds() / 1000, 0, int.MaxValue);

    public string GetState()
    {
        string newState;
        if (_locked || _suspended)
            newState = "away";
        else if (IsIdle())
            newState = "idle";
        else
            newState = "active";

        if (newState != _lastState)
        {
            var from = _lastState;
            _lastState = newState;
            StateChanged?.Invoke(from, newState);
        }
        return newState;
    }
}
