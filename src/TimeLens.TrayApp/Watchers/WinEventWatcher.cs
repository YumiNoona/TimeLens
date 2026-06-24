namespace TimeLens.TrayApp.Watchers;

public sealed class WinEventWatcher : IDisposable
{
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint EVENT_OBJECT_NAMECHANGE = 0x800C;
    private const int OBJID_WINDOW = 0x0000;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;

    private readonly Win32.WinEventDelegate _hookDelegate;
    private IntPtr _fgHook;
    private IntPtr _nameHook;
    private readonly Dictionary<int, string> _pidCache = new(200);
    private const int MaxCacheSize = 200;

    public event Action<string, string, int>? ForegroundChanged;

    public WinEventWatcher()
    {
        _hookDelegate = OnWinEvent;
    }

    public void Start()
    {
        _fgHook = Win32.SetWinEventHook(
            EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _hookDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);

        _nameHook = Win32.SetWinEventHook(
            EVENT_OBJECT_NAMECHANGE, EVENT_OBJECT_NAMECHANGE,
            IntPtr.Zero, _hookDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);

        if (_fgHook == IntPtr.Zero)
            throw new InvalidOperationException("Failed to install foreground hook.");

        // Fire initial event for the current foreground window
        var hwnd = Win32.GetForegroundWindow();
        if (hwnd != IntPtr.Zero)
            OnWinEvent(IntPtr.Zero, 0, hwnd, 0, 0, 0, 0);
    }

    private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (hwnd == IntPtr.Zero) return;

        // For name-change events, only fire for top-level windows, and only if
        // they belong to the same PID as the current foreground window.
        if (eventType == EVENT_OBJECT_NAMECHANGE)
        {
            if (idObject != OBJID_WINDOW) return;
            var fgHwnd = Win32.GetForegroundWindow();
            Win32.GetWindowThreadProcessId(fgHwnd, out var fgPid);
            Win32.GetWindowThreadProcessId(hwnd, out var changedPid);
            if (fgPid != changedPid) return;
        }

        var sb = new System.Text.StringBuilder(256);
        Win32.GetWindowText(hwnd, sb, sb.Capacity);
        var title = sb.ToString();

        Win32.GetWindowThreadProcessId(hwnd, out var pid);

        var exeName = ResolveExeName((int)pid);

        ForegroundChanged?.Invoke(exeName, title, (int)pid);
    }

    private string ResolveExeName(int pid)
    {
        if (_pidCache.TryGetValue(pid, out var cached))
            return cached;

        string name;
        try
        {
            using var proc = System.Diagnostics.Process.GetProcessById(pid);
            name = proc.ProcessName + ".exe";
        }
        catch
        {
            name = "unknown";
        }

        if (_pidCache.Count >= MaxCacheSize)
            _pidCache.Clear();

        _pidCache[pid] = name;
        return name;
    }

    public void Dispose()
    {
        if (_fgHook != IntPtr.Zero) Win32.UnhookWinEvent(_fgHook);
        if (_nameHook != IntPtr.Zero) Win32.UnhookWinEvent(_nameHook);
    }
}
