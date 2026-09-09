using System.Runtime.InteropServices;

namespace TimeLens.TrayApp.Watchers;

public sealed class InputMonitor : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    private static readonly LowLevelKeyboardProc KeyboardProc = KeyboardHookCallback;
    private static readonly LowLevelMouseProc MouseProc = MouseHookCallback;

    private readonly object _countsLock = new();
    private readonly Dictionary<(int Pid, string Exe, DateTime ObservedAt), (int Keys, int Clicks)> _counts = new();
    private static InputMonitor? _instance;

    private IntPtr _keyboardHook;
    private IntPtr _mouseHook;
    private Timer? _flushTimer;

    public event Action<int, int, int?, string?, DateTime>? InputActivityTick;

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowsHookEx(int idHook, IntPtr lpfn, IntPtr hmod, uint dwThreadId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;

    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_MBUTTONDOWN = 0x0207;

    public void Start()
    {
        _instance = this;
        using var curProc = System.Diagnostics.Process.GetCurrentProcess();
        using var mainModule = curProc.MainModule!;
        var moduleHandle = GetModuleHandle(mainModule.ModuleName);

        _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL,
            Marshal.GetFunctionPointerForDelegate(KeyboardProc), moduleHandle, 0);
        _mouseHook = SetWindowsHookEx(WH_MOUSE_LL,
            Marshal.GetFunctionPointerForDelegate(MouseProc), moduleHandle, 0);

        _flushTimer = new Timer(FlushCounters, null, 5_000, 5_000);
    }

    private void FlushCounters(object? state) => RuntimeDiagnostics.TryRun("Input timer", Flush);

    internal void RecordInput(int pid, string exe, int keys, int clicks, DateTime? observedAt = null)
    {
        var captured = (observedAt ?? DateTime.UtcNow).ToUniversalTime();
        var second = new DateTime(captured.Ticks - captured.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
        lock (_countsLock)
        {
            var current = _counts.GetValueOrDefault((pid, exe, second));
            _counts[(pid, exe, second)] = (current.Keys + keys, current.Clicks + clicks);
        }
    }

    private void CaptureInput(int keys, int clicks) => RuntimeDiagnostics.TryRun("Input capture", () =>
    {
        var (exe, _, pid) = Win32.GetForegroundWindowInfo();
        RecordInput(pid, exe, keys, clicks);
    });

    internal void Flush()
    {
        KeyValuePair<(int Pid, string Exe, DateTime ObservedAt), (int Keys, int Clicks)>[] batch;
        lock (_countsLock) { batch = _counts.ToArray(); _counts.Clear(); }
        foreach (var entry in batch)
            if (!PublishInput(entry.Value.Keys, entry.Value.Clicks, entry.Key.Pid, entry.Key.Exe, entry.Key.ObservedAt))
                RecordInput(entry.Key.Pid, entry.Key.Exe, entry.Value.Keys, entry.Value.Clicks, entry.Key.ObservedAt);
    }

    internal bool PublishInput(int keys, int clicks, int? pid, string? exe, DateTime? observedAt = null) =>
        RuntimeDiagnostics.TryRun("Input subscriber", () => InputActivityTick?.Invoke(keys, clicks, pid, exe, observedAt ?? DateTime.UtcNow));

    private static IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam.ToInt32() == 0x0100 || wParam.ToInt32() == 0x0104) && _instance is { } inst)
            inst.CaptureInput(1, 0);
        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private static IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _instance is { } inst)
        {
            var msg = wParam.ToInt32();
            if (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN || msg == WM_MBUTTONDOWN)
                inst.CaptureInput(0, 1);
        }
        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    public void Stop()
    {
        _flushTimer?.Dispose();
        _flushTimer = null;
        if (_keyboardHook != IntPtr.Zero) { UnhookWindowsHookEx(_keyboardHook); _keyboardHook = IntPtr.Zero; }
        if (_mouseHook != IntPtr.Zero) { UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
        Flush();
        _instance = null;
    }

    public void Dispose() => Stop();
}
