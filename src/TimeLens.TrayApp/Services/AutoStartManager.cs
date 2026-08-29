using Microsoft.Win32;

namespace TimeLens.TrayApp.Services;

public static class AutoStartManager
{
    internal const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string ValueName = "TimeLens";

    public static bool IsAutoStartEnabled()
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath)) return false;
        return IsAutoStartEnabled(Registry.CurrentUser, RunKeyPath, ValueName, executablePath);
    }

    public static bool TrySetAutoStart(bool enabled, out string? error)
    {
        error = null;
        try
        {
            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath))
                throw new InvalidOperationException("The TimeLens executable path is unavailable.");
            SetAutoStart(Registry.CurrentUser, RunKeyPath, ValueName, executablePath, enabled);
            if (IsAutoStartEnabled() != enabled)
                throw new InvalidOperationException("Windows did not preserve the startup setting.");
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    internal static bool IsAutoStartEnabled(
        RegistryKey root, string runKeyPath, string valueName, string executablePath)
    {
        try
        {
            using var key = root.OpenSubKey(runKeyPath, writable: false);
            var command = key?.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
            var quotedPath = $"\"{Path.GetFullPath(executablePath)}\"";
            return string.Equals(command, quotedPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(command, $"{quotedPath} --startup", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    internal static void SetAutoStart(
        RegistryKey root, string runKeyPath, string valueName, string executablePath, bool enabled)
    {
        using var key = root.CreateSubKey(runKeyPath, writable: true)
            ?? throw new InvalidOperationException("Windows could not open the current-user startup registry key.");
        if (enabled)
            key.SetValue(valueName, $"\"{Path.GetFullPath(executablePath)}\" --startup", RegistryValueKind.String);
        else
            key.DeleteValue(valueName, throwOnMissingValue: false);
        key.Flush();
    }
}
