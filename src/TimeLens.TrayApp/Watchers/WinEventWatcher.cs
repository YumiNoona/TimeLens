using System.Runtime.InteropServices;

namespace TimeLens.TrayApp.Watchers;

public sealed class WinEventWatcher : IDisposable
{
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;

    private readonly WinEventDelegate _hookDelegate;
    private IntPtr _hookHandle;

    public event Action<string, string, int>? ForegroundChanged;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void WinEventDelegate(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    [DllImport("user32.dll")]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    public WinEventWatcher()
    {
        _hookDelegate = OnWinEvent;
    }

    public void Start()
    {
        _hookHandle = SetWinEventHook(
            EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _hookDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);

        if (_hookHandle == IntPtr.Zero)
            throw new InvalidOperationException("Failed to install foreground hook.");

        // Fire initial event for the current foreground window
        var hwnd = GetForegroundWindow();
        if (hwnd != IntPtr.Zero)
            OnWinEvent(IntPtr.Zero, 0, hwnd, 0, 0, 0, 0);
    }

    private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (hwnd == IntPtr.Zero) return;

        var sb = new System.Text.StringBuilder(256);
        GetWindowText(hwnd, sb, sb.Capacity);
        var title = sb.ToString();

        GetWindowThreadProcessId(hwnd, out var pid);

        var exeName = "unknown";
        try
        {
            using var proc = System.Diagnostics.Process.GetProcessById((int)pid);
            exeName = proc.ProcessName + ".exe";
        }
        catch { }

        ForegroundChanged?.Invoke(exeName, title, (int)pid);
    }

    public void Dispose()
    {
        if (_hookHandle != IntPtr.Zero)
            UnhookWinEvent(_hookHandle);
    }
}
