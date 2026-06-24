using Microsoft.Win32;

namespace TimeLens.TrayApp.Services;

public static class AutoStartManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TimeLens";

    public static bool IsAutoStartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(ValueName) is not null;
        }
        catch
        {
            return false;
        }
    }

    public static void SetAutoStart(bool enabled)
    {
        try
        {
            var exePath = Environment.ProcessPath!;
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key is null) return;

            if (enabled)
                key.SetValue(ValueName, $"\"{exePath}\"");
            else
                key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch
        {
            // Non-critical
        }
    }
}
