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
    private readonly Queue<int> _pidCacheOrder = new();
    private const int MaxCacheSize = 200;

    public event Action<string, string, int>? ForegroundChanged;

    private string _lastExe = "";
    private string _lastTitle = "";
    private long _lastFireTicks;

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

        var sb = new System.Text.StringBuilder(256);
        Win32.GetWindowText(hwnd, sb, sb.Capacity);
        var title = sb.ToString();

        Win32.GetWindowThreadProcessId(hwnd, out var pid);

        var exeName = ResolveExeName((int)pid);

        // Debounce name-change events — VS Code fires hundreds per minute
        // when switching files. Skip writes when title hasn't changed and
        // less than 5 seconds have passed since the last write for this exe.
        if (eventType == EVENT_OBJECT_NAMECHANGE)
        {
            if (idObject != OBJID_WINDOW) return;
            var fgHwnd = Win32.GetForegroundWindow();
            Win32.GetWindowThreadProcessId(fgHwnd, out var fgPid);
            Win32.GetWindowThreadProcessId(hwnd, out var changedPid);
            if (fgPid != changedPid) return;

            if (string.Equals(exeName, _lastExe, StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(title, _lastTitle, StringComparison.Ordinal)) return;
                var nowTicks = Environment.TickCount64;
                if (nowTicks - _lastFireTicks < 5_000) return;
                _lastFireTicks = nowTicks;
            }
        }

        _lastExe = exeName;
        _lastTitle = title;

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
        {
            var oldest = _pidCacheOrder.Dequeue();
            _pidCache.Remove(oldest);
        }

        _pidCache[pid] = name;
        _pidCacheOrder.Enqueue(pid);
        return name;
    }

    public void Dispose()
    {
        if (_fgHook != IntPtr.Zero) Win32.UnhookWinEvent(_fgHook);
        if (_nameHook != IntPtr.Zero) Win32.UnhookWinEvent(_nameHook);
    }
}
