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
        RuntimeDiagnostics.TryRun("Foreground callback", () =>
            HandleWinEvent(eventType, hwnd, idObject));
    }

    private void HandleWinEvent(uint eventType, IntPtr hwnd, int idObject)
    {
        if (hwnd == IntPtr.Zero || hwnd != Win32.GetForegroundWindow()) return;

        if (eventType == EVENT_OBJECT_NAMECHANGE && idObject != OBJID_WINDOW) return;
        // Persist every real switch; identical observations are coalesced by the writer.
        // Resolve the live process each time because Windows reuses process IDs.
        var (exe, title, pid) = Win32.GetForegroundWindowInfo();
        PublishForeground(exe, title, pid);
    }

    internal bool PublishForeground(string exe, string title, int pid) =>
        RuntimeDiagnostics.TryRun("Foreground subscriber", () => ForegroundChanged?.Invoke(exe, title, pid));

    public void Dispose()
    {
        if (_fgHook != IntPtr.Zero) Win32.UnhookWinEvent(_fgHook);
        if (_nameHook != IntPtr.Zero) Win32.UnhookWinEvent(_nameHook);
    }
}
