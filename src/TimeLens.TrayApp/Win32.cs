using System.Runtime.InteropServices;

namespace TimeLens.TrayApp;

internal static class Win32
{
    public const int SW_MINIMIZE = 6;
    public const int SW_HIDE = 0;
    public const int SW_RESTORE = 9;
    public const int SW_SHOWNOACTIVATE = 4;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void WinEventDelegate(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    public delegate bool EnumWindowDelegate(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowDelegate lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern IntPtr SetWinEventHook(
        uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc,
        uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    public static (string exe, string title, int pid) GetForegroundWindowInfo()
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

    public static List<IntPtr> FindWindowsForProcess(string exeName)
    {
        var target = exeName.Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
        var windows = new List<IntPtr>();

        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd))
                return true;

            GetWindowThreadProcessId(hwnd, out var pid);
            try
            {
                using var proc = System.Diagnostics.Process.GetProcessById((int)pid);
                if (string.Equals(proc.ProcessName, target, StringComparison.OrdinalIgnoreCase))
                {
                    var sb = new System.Text.StringBuilder(256);
                    GetWindowText(hwnd, sb, sb.Capacity);
                    if (sb.Length > 0)
                        windows.Add(hwnd);
                }
            }
            catch { }
            return true;
        }, IntPtr.Zero);

        return windows;
    }
}
