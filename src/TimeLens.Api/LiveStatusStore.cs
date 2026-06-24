namespace TimeLens.Api;

public static class LiveStatusStore
{
    public static string CurrentApp { get; set; } = "—";
    public static int IdleSeconds { get; set; }
    public static bool IsIdle { get; set; }
    public static string? AudibleTab { get; set; }
    public static bool AudioActive { get; set; }
    public static string SystemState { get; set; } = "active";
    public static bool PendingIdleReturn { get; set; }
    public static AppSettings Settings { get; set; } = new();
}
