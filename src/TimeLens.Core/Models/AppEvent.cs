namespace TimeLens.Core.Models;

public sealed class AppEvent
{
    public long Id { get; set; }
    public string ExeName { get; set; } = string.Empty;
    public string? WindowTitle { get; set; }
    public int Pid { get; set; }
    public string? Category { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public bool WasIdle { get; set; }
}
