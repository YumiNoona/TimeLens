using TimeLens.Api;

namespace TimeLens.TrayApp;

internal static class RuntimeConfig
{
    public static AppSettings Settings { get; set; } = new();
}
