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

    public event Action<string, string>? StateChanged;

    private string _lastState = "active";

    private bool IsAudioActive()
    {
        if (AudioMonitorRef is not null && AudioMonitorRef.AnyAudioPlaying) return true;
        if (!string.IsNullOrEmpty(TimeLens.Api.LiveStatusStore.AudibleTab)) return true;
        return false;
    }

    private static long ElapsedSince(uint dwTime)
    {
        long now = Environment.TickCount64;
        long then = (now & ~0xFFFFFFFFL) | dwTime;
        if (then > now) then -= 0x100000000L;
        return now - then;
    }

    public bool IsIdle()
    {
        if (IsAudioActive()) return false;

        var lii = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref lii)) return false;

        return ElapsedSince(lii.dwTime) >= IdleThresholdSeconds * 1000;
    }

    public int IdleSeconds()
    {
        if (IsAudioActive()) return 0;

        var lii = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref lii)) return 0;

        return (int)(ElapsedSince(lii.dwTime) / 1000);
    }

    internal void ResetLastState()
    {
        _lastState = "active";
    }

    public string GetState()
    {
        var sysState = TimeLens.Api.LiveStatusStore.SystemState;
        string newState;
        if (sysState == "away")
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
