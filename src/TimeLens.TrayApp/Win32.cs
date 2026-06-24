using System.Runtime.InteropServices;

namespace TimeLens.TrayApp;

internal static class Win32
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void WinEventDelegate(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

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
}
