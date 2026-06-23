namespace TimeLens.Core.Models;

public sealed class InputActivity
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    public int KeystrokeCount { get; set; }
    public int ClickCount { get; set; }
    public int? Pid { get; set; }
    public string? ExeName { get; set; }
}
