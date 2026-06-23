using Microsoft.Win32;

namespace TimeLens.TrayApp.Services;

public static class AutoStartManager
{
    public static void EnsureAutoStart()
    {
        try
        {
            var exePath = Environment.ProcessPath!;
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            key?.SetValue("TimeLens", $"\"{exePath}\"");
        }
        catch
        {
            // Non-critical — user can re-enable manually
        }
    }
}
