namespace TimeLens.Core.Models;

public sealed class BrowserEvent
{
    public long Id { get; set; }
    public string Domain { get; set; } = string.Empty;
    public string? Url { get; set; }
    public string? Title { get; set; }
    public string? Category { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string Browser { get; set; } = string.Empty;
}
