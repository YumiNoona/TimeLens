namespace TimeLens.Api;

public static class LiveStatusStore
{
    public static string CurrentApp { get; set; } = "—";
    public static int IdleSeconds { get; set; }
    public static bool IsIdle { get; set; }
    public static string? AudibleTab { get; set; }
public static bool AudioActive { get; set; }
}
