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

    private static int _keyCount;
    private static int _clickCount;

    private IntPtr _keyboardHook;
    private IntPtr _mouseHook;
    private Timer? _flushTimer;

    public event Action<int, int, int?, string?>? InputActivityTick;

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
        using var curProc = System.Diagnostics.Process.GetCurrentProcess();
        using var mainModule = curProc.MainModule!;
        var moduleHandle = GetModuleHandle(mainModule.ModuleName);

        _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL,
            Marshal.GetFunctionPointerForDelegate(KeyboardProc), moduleHandle, 0);
        _mouseHook = SetWindowsHookEx(WH_MOUSE_LL,
            Marshal.GetFunctionPointerForDelegate(MouseProc), moduleHandle, 0);

        _flushTimer = new Timer(FlushCounters, null, 60_000, 60_000);
    }

    private void FlushCounters(object? state)
    {
        var k = Interlocked.Exchange(ref _keyCount, 0);
        var c = Interlocked.Exchange(ref _clickCount, 0);

        if (k > 0 || c > 0)
            InputActivityTick?.Invoke(k, c, null, null);
    }

    private static IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
            Interlocked.Increment(ref _keyCount);
        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private static IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var msg = wParam.ToInt32();
            if (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN || msg == WM_MBUTTONDOWN)
                Interlocked.Increment(ref _clickCount);
        }
        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        _flushTimer?.Dispose();
        if (_keyboardHook != IntPtr.Zero) UnhookWindowsHookEx(_keyboardHook);
        if (_mouseHook != IntPtr.Zero) UnhookWindowsHookEx(_mouseHook);
    }
}
